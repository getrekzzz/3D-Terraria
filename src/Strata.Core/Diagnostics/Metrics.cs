using System.Collections.Concurrent;
using System.Diagnostics;

namespace Strata.Core.Diagnostics;

/// <summary>Thread-safe counters and timing accumulators for profiling overlays and reports.</summary>
public sealed class Metrics
{
    private readonly ConcurrentDictionary<string, Accumulator> _timers = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();

    public static readonly Metrics Global = new();

    /// <summary>When false, <see cref="Time"/> scopes cost almost nothing and record nothing (profiling is opt-in).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The frame/tick profiler: off until the <c>perf</c> command turns it on.</summary>
    public static readonly Metrics Frame = new() { Enabled = false };

    public void Count(string name, long delta = 1) => _counters.AddOrUpdate(name, delta, (_, v) => v + delta);

    public long Counter(string name) => _counters.TryGetValue(name, out var v) ? v : 0;

    public void Record(string name, double milliseconds) => _timers.GetOrAdd(name, _ => new Accumulator()).Add(milliseconds);

    public TimerScope Time(string name) => Enabled ? new(this, name) : default;

    public IReadOnlyDictionary<string, (long Count, double TotalMs, double MaxMs)> SnapshotTimers() =>
        _timers.ToDictionary(kv => kv.Key, kv => kv.Value.Snapshot());

    public IReadOnlyDictionary<string, long> SnapshotCounters() => _counters.ToDictionary(kv => kv.Key, kv => kv.Value);

    public void Reset()
    {
        _timers.Clear();
        _counters.Clear();
    }

    public readonly struct TimerScope : IDisposable
    {
        private readonly Metrics _owner;
        private readonly string _name;
        private readonly long _start;

        public TimerScope(Metrics owner, string name)
        {
            _owner = owner;
            _name = name;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose() => _owner?.Record(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }

    private sealed class Accumulator
    {
        private readonly object _gate = new();
        private long _count;
        private double _total;
        private double _max;

        public void Add(double ms)
        {
            lock (_gate)
            {
                _count++;
                _total += ms;
                if (ms > _max) _max = ms;
            }
        }

        public (long, double, double) Snapshot()
        {
            lock (_gate) return (_count, _total, _max);
        }
    }
}
