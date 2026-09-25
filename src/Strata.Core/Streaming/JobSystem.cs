using System.Collections.Concurrent;
using Strata.Core.Diagnostics;

namespace Strata.Core.Streaming;

/// <summary>
/// Dedicated worker threads executing prioritized background jobs (lower priority value runs first).
/// Results are posted to a completion queue drained on the main thread. Exceptions are captured and
/// reported, never swallowed.
/// </summary>
public sealed class JobSystem : IDisposable
{
    private readonly PriorityQueue<Job, (int, long)> _queue = new();
    private readonly object _gate = new();
    private readonly List<Thread> _threads = new();
    private readonly ILog _log;
    private long _sequence;
    private bool _stopping;
    private int _running;

    public JobSystem(int workers, ILog log)
    {
        _log = log;
        for (int i = 0; i < Math.Max(1, workers); i++)
        {
            var t = new Thread(WorkerLoop) { IsBackground = true, Name = $"strata-worker-{i}", Priority = ThreadPriority.BelowNormal };
            _threads.Add(t);
            t.Start();
        }
    }

    public ConcurrentQueue<Action> Completions { get; } = new();

    public int Pending
    {
        get { lock (_gate) return _queue.Count; }
    }

    public int Running => Volatile.Read(ref _running);

    public bool Idle => Pending == 0 && Running == 0 && Completions.IsEmpty;

    /// <summary>
    /// Schedules work; <paramref name="complete"/> runs later on the thread that drains completions.
    /// If the work throws, the error is logged and <paramref name="complete"/> receives <c>default</c> so
    /// callers can release their bookkeeping.
    /// </summary>
    public void Schedule<T>(int priority, Func<T> work, Action<T?> complete, string label)
    {
        var job = new Job(label, () =>
        {
            T? result = default;
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                _log.Error("jobs", $"Job '{label}' failed", ex);
            }
            Completions.Enqueue(() => complete(result));
        });
        lock (_gate)
        {
            _queue.Enqueue(job, (priority, _sequence++));
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Runs queued completions on the calling (main) thread, up to a time budget.</summary>
    public int Drain(double budgetMs)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        int n = 0;
        while (Completions.TryDequeue(out var action))
        {
            action();
            n++;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds > budgetMs) break;
        }
        return n;
    }

    private void WorkerLoop()
    {
        while (true)
        {
            Job job;
            lock (_gate)
            {
                while (_queue.Count == 0 && !_stopping) Monitor.Wait(_gate);
                if (_stopping) return;
                job = _queue.Dequeue();
                Interlocked.Increment(ref _running);
            }
            try
            {
                job.Run();
            }
            catch (Exception ex)
            {
                _log.Error("jobs", $"Job '{job.Label}' failed", ex);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            _queue.Clear();
            Monitor.PulseAll(_gate);
        }
        foreach (var t in _threads) t.Join(2000);
    }

    private sealed record Job(string Label, Action Run);
}
