using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Strata.Core.Diagnostics;

/// <summary>
/// Hitch finder: per-frame timer totals and the slowest recent frames. While <see cref="Metrics.TrackFrames"/> is on,
/// every recorded timer also adds into the current frame (<see cref="Add"/>, lock-free); <see cref="EndFrame"/> closes
/// the frame with its real duration, counts slow frames, keeps a frame-time histogram (percentiles) and stores frames
/// of <see cref="SpikeMs"/> or more as <see cref="Spike"/>s with their five most expensive timers and the garbage
/// collections that ran during them. Closing a frame allocates nothing unless it is a spike.
/// A spike lists causes, not containers: frame lengths and enclosing totals are left out, and so is a timer whose
/// dotted child explains it (<c>gd.colliders</c> when <c>gd.colliders.build</c> took all but a millisecond of it).
/// </summary>
public sealed class SpikeLog
{
    /// <summary>Spikes kept (the oldest is overwritten).</summary>
    public const int Capacity = 64;

    /// <summary>Timers listed per spike.</summary>
    public const int TopCount = 5;

    /// <summary>Frame-time thresholds (ms): a spike (under 30 fps), a bad frame, a severe hitch.</summary>
    public const double SpikeMs = 33.0, BadMs = 50.0, SevereMs = 100.0;

    /// <summary>
    /// Timers never listed in a spike: <c>frame.delta</c> is the frame's own length; <c>tick.total</c> and
    /// <c>gd.world_runtime</c> enclose timers that are listed on their own (they stay in <c>perf report</c>).
    /// </summary>
    private static readonly HashSet<string> Unlisted = new(StringComparer.Ordinal) { "frame.delta", "tick.total", "gd.world_runtime" };

    /// <summary>A parent timer whose dotted child is within this of it adds nothing to a spike's list (ms).</summary>
    private const double ExplainedMs = 1.0;

    /// <summary>Candidates kept per frame before parents are pruned (twice the list, so a pruned parent leaves room).</summary>
    private const int Candidates = TopCount * 2;

    // Frame-time histogram: 0.5 ms steps up to 100 ms (where frame budgets live), then 10 ms steps up to 10 s (software
    // rendering, loading stalls); longer frames land in the last bucket, which reports the worst frame.
    private const double FineMs = 0.5, FineLimitMs = 100.0, CoarseMs = 10.0;
    private const int FineBuckets = 200, Buckets = FineBuckets + 990;

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly object _grow = new();
    private Slot[] _order = Array.Empty<Slot>(); // copy-on-write, so EndFrame walks it without locking

    private readonly object _gate = new();
    private readonly Spike?[] _ring = new Spike?[Capacity];
    private readonly int[] _histogram = new int[Buckets];
    private readonly string[] _topNames = new string[Candidates];
    private readonly double[] _topMs = new double[Candidates];
    private int _next;
    private long _frames, _over33, _over50, _over100;
    private double _worst;
    private GcSample _start, _last;

    public SpikeLog() => _start = _last = GcSample.Now();

    /// <summary>
    /// Set by the engine when frames are measured without their draw (software rendering takes seconds per frame, which
    /// would make every frame a spike): frame times are then the main thread's own work. Survives resets.
    /// </summary>
    public bool MainThreadOnly { get; set; }

    /// <summary>A frame of <see cref="SpikeMs"/> or more: what ran longest in it and the GC work done meanwhile.</summary>
    /// <param name="Frame">Index of the frame since the last reset (1 = first closed frame).</param>
    public sealed record Spike(long Frame, double Ms, IReadOnlyList<(string Timer, double Ms)> Top, int Gen0, int Gen1, int Gen2, double GcPauseMs)
    {
        /// <summary>"72.0 ms: save.capture 40.1, gd.colliders 12.0 · GC gen0 gen2 pause 8.0 ms".</summary>
        public string Describe()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(inv, $"{Ms:0.0} ms");
            for (int i = 0; i < Top.Count; i++) sb.Append(i == 0 ? ": " : ", ").Append(inv, $"{Top[i].Timer} {Top[i].Ms:0.0}");
            if (Gen0 + Gen1 + Gen2 > 0)
            {
                sb.Append(" · GC");
                if (Gen0 > 0) sb.Append(" gen0");
                if (Gen1 > 0) sb.Append(" gen1");
                if (Gen2 > 0) sb.Append(" gen2");
                sb.Append(inv, $" pause {GcPauseMs:0.0} ms");
            }
            return sb.ToString();
        }
    }

    /// <summary>Frames closed since the last reset.</summary>
    public long Frames { get { lock (_gate) return _frames; } }

    public long Over33 { get { lock (_gate) return _over33; } }

    public long Over50 { get { lock (_gate) return _over50; } }

    public long Over100 { get { lock (_gate) return _over100; } }

    /// <summary>Longest frame since the last reset (ms).</summary>
    public double WorstMs { get { lock (_gate) return _worst; } }

    /// <summary>Garbage collections per generation since the last reset (a gen2 collection also counts in 0 and 1).</summary>
    public (int Gen0, int Gen1, int Gen2) Collections
    {
        get
        {
            lock (_gate) return (_last.Gen0 - _start.Gen0, _last.Gen1 - _start.Gen1, _last.Gen2 - _start.Gen2);
        }
    }

    /// <summary>Time the runtime was paused for garbage collection since the last reset (ms).</summary>
    public double GcPauseMs { get { lock (_gate) return _last.PauseMs - _start.PauseMs; } }

    /// <summary>Adds a timer's milliseconds to the current frame (any thread).</summary>
    public void Add(string name, double ms)
    {
        if (!_slots.TryGetValue(name, out var slot)) slot = AddSlot(name);
        AddDouble(ref slot.FrameMs, ms);
        Interlocked.Increment(ref slot.FrameCalls);
    }

    /// <summary>Closes the current frame (once per rendered frame, on the main thread).</summary>
    public void EndFrame(double frameMs)
    {
        int top = 0;
        var order = Volatile.Read(ref _order);
        for (int i = 0; i < order.Length; i++)
        {
            var s = order[i];
            double ms = Interlocked.Exchange(ref s.FrameMs, 0);
            int calls = Interlocked.Exchange(ref s.FrameCalls, 0);
            if (calls > Volatile.Read(ref s.PeakCalls)) Volatile.Write(ref s.PeakCalls, calls);
            if (ms > 0 && s.Listed) top = InsertTop(s.Name, ms, top);
        }
        var gc = GcSample.Now();
        lock (_gate)
        {
            _frames++;
            if (frameMs > _worst) _worst = frameMs;
            _histogram[Bucket(frameMs)]++;
            if (frameMs >= SpikeMs)
            {
                _over33++;
                if (frameMs >= BadMs) _over50++;
                if (frameMs >= SevereMs) _over100++;
                _ring[_next] = new Spike(_frames, frameMs, Causes(top), gc.Gen0 - _last.Gen0, gc.Gen1 - _last.Gen1, gc.Gen2 - _last.Gen2, gc.PauseMs - _last.PauseMs);
                _next = (_next + 1) % Capacity;
            }
            _last = gc;
        }
    }

    /// <summary>The most recent spikes, newest first.</summary>
    public IReadOnlyList<Spike> Recent(int count = Capacity)
    {
        var list = new List<Spike>();
        lock (_gate)
        {
            for (int i = 1; i <= Capacity && list.Count < count; i++)
                if (_ring[(_next - i + Capacity) % Capacity] is { } s) list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// Frame time (ms) under which a fraction <paramref name="p"/> of the closed frames fall: the upper edge of a histogram
    /// step (0.5 ms under 100 ms, 10 ms above), never more than the worst frame.
    /// </summary>
    public double Percentile(double p)
    {
        lock (_gate)
        {
            if (_frames == 0) return 0;
            long rank = Math.Max(1, (long)Math.Ceiling(_frames * Math.Clamp(p, 0, 1) - 1e-9));
            long seen = 0;
            for (int i = 0; i < Buckets - 1; i++)
            {
                seen += _histogram[i];
                if (seen >= rank) return Math.Min(UpperEdge(i), _worst);
            }
            return _worst;
        }
    }

    /// <summary>The most calls a timer made within one closed frame (e.g. colliders built in one frame).</summary>
    public int PeakCalls(string name) => _slots.TryGetValue(name, out var s) ? Volatile.Read(ref s.PeakCalls) : 0;

    /// <summary>Forgets every frame, spike and peak; collections are counted from now.</summary>
    public void Reset()
    {
        foreach (var s in Volatile.Read(ref _order))
        {
            Interlocked.Exchange(ref s.FrameMs, 0);
            Interlocked.Exchange(ref s.FrameCalls, 0);
            Volatile.Write(ref s.PeakCalls, 0);
        }
        lock (_gate)
        {
            Array.Clear(_ring);
            Array.Clear(_histogram);
            _next = 0;
            _frames = _over33 = _over50 = _over100 = 0;
            _worst = 0;
            _start = _last = GcSample.Now();
        }
    }

    /// <summary>Tracking resumes after a pause: collections made while nobody watched are not blamed on the next frame.</summary>
    public void Resume()
    {
        var gc = GcSample.Now();
        lock (_gate)
        {
            _start = new GcSample(_start.Gen0 + gc.Gen0 - _last.Gen0, _start.Gen1 + gc.Gen1 - _last.Gen1, _start.Gen2 + gc.Gen2 - _last.Gen2, _start.PauseMs + gc.PauseMs - _last.PauseMs);
            _last = gc;
        }
    }

    /// <summary>Summary line: "spikes >=33 ms 12 (>=50 5, >=100 1) of 3000 frames · p50 … p99 … max … · GC 10/2/1 pause 30.2 ms".</summary>
    public string Summary()
    {
        var inv = CultureInfo.InvariantCulture;
        var (g0, g1, g2) = Collections;
        string frames = MainThreadOnly ? "frames (main thread, draw excluded)" : "frames";
        return string.Create(inv, $"spikes >=33 ms {Over33} (>=50 {Over50}, >=100 {Over100}) of {Frames} {frames} · p50 {Percentile(0.5):0.0} p95 {Percentile(0.95):0.0} p99 {Percentile(0.99):0.0} max {WorstMs:0.0} ms · GC {g0}/{g1}/{g2} pause {GcPauseMs:0.0} ms");
    }

    /// <summary>Keeps the <see cref="Candidates"/> largest entries sorted by ms (insertion into a tiny array, no allocation).</summary>
    private int InsertTop(string name, double ms, int count)
    {
        if (count == Candidates && ms <= _topMs[Candidates - 1]) return count;
        int i = Math.Min(count, Candidates - 1);
        while (i > 0 && _topMs[i - 1] < ms)
        {
            _topNames[i] = _topNames[i - 1];
            _topMs[i] = _topMs[i - 1];
            i--;
        }
        _topNames[i] = name;
        _topMs[i] = ms;
        return Math.Min(count + 1, Candidates);
    }

    /// <summary>The spike's list: up to <see cref="TopCount"/> candidates, skipping parents their listed child explains.</summary>
    private (string, double)[] Causes(int candidates)
    {
        var list = new List<(string, double)>(TopCount);
        for (int i = 0; i < candidates && list.Count < TopCount; i++)
        {
            bool explained = false;
            for (int j = 0; j < candidates && !explained; j++)
                explained = j != i && _topMs[j] >= _topMs[i] - ExplainedMs && IsChild(_topNames[j], _topNames[i]);
            if (!explained) list.Add((_topNames[i], _topMs[i]));
        }
        return list.ToArray();
    }

    private static int Bucket(double ms) => ms < FineLimitMs
        ? Math.Max(0, (int)(ms / FineMs))
        : Math.Min(Buckets - 1, FineBuckets + (int)((ms - FineLimitMs) / CoarseMs));

    private static double UpperEdge(int bucket) => bucket < FineBuckets
        ? (bucket + 1) * FineMs
        : FineLimitMs + (bucket - FineBuckets + 1) * CoarseMs;

    private static bool IsChild(string name, string parent) =>
        name.Length > parent.Length + 1 && name[parent.Length] == '.' && name.StartsWith(parent, StringComparison.Ordinal);

    private Slot AddSlot(string name)
    {
        lock (_grow)
        {
            if (_slots.TryGetValue(name, out var existing)) return existing;
            var slot = new Slot(name, !Unlisted.Contains(name));
            var order = new Slot[_order.Length + 1];
            Array.Copy(_order, order, _order.Length);
            order[^1] = slot;
            Volatile.Write(ref _order, order);
            _slots[name] = slot;
            return slot;
        }
    }

    private static void AddDouble(ref double target, double value)
    {
        double seen = Volatile.Read(ref target);
        while (true)
        {
            double was = Interlocked.CompareExchange(ref target, seen + value, seen);
            if (BitConverter.DoubleToInt64Bits(was) == BitConverter.DoubleToInt64Bits(seen)) return;
            seen = was;
        }
    }

    private sealed class Slot(string name, bool listed)
    {
        public readonly string Name = name;
        public readonly bool Listed = listed;
        public double FrameMs;
        public int FrameCalls;
        public int PeakCalls;
    }

    private readonly record struct GcSample(int Gen0, int Gen1, int Gen2, double PauseMs)
    {
        public static GcSample Now() =>
            new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds);
    }
}
