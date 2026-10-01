using System.Globalization;
using System.Text;
using Strata.Core.Diagnostics;

namespace Strata.Core.Commands;

/// <summary>
/// The frame profiler: <c>perf start</c> clears and enables it, <c>perf report</c> prints where the time went and
/// <c>perf spikes</c> lists the slowest recent frames with their most expensive timers (probes in <see cref="PerfProbes"/>).
/// </summary>
public static class PerfCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "perf", Usage = "perf <start|stop|report [count]|spikes [count]>", Help = "Profiles simulation ticks and engine frames; lists frame-time spikes.",
            Run = (ctx, a) =>
            {
                var m = Metrics.Frame;
                switch (a.Str(0, "action"))
                {
                    case "start":
                        m.Reset();
                        m.Enabled = true;
                        m.TrackFrames = true;
                        return CommandResult.Success("Profiling.");
                    case "stop":
                        m.Enabled = false;
                        m.TrackFrames = false;
                        return CommandResult.Success("Profiling stopped.");
                    case "report":
                        return CommandResult.Success(Report(m, a.OptInt(1, 30)));
                    case "spikes":
                        return CommandResult.Success(Spikes(m.Spikes, a.OptInt(1, 10)));
                    default:
                        throw new CommandException("expected start, stop, report or spikes");
                }
            },
        });
    }

    /// <summary>Spike and GC counts, then per-frame averages (ms per rendered frame), average and worst per call, sorted by total cost.</summary>
    public static string Report(Metrics m, int count)
    {
        var timers = m.SnapshotTimers();
        long frames = timers.TryGetValue("frame.delta", out var fd) ? Math.Max(1, fd.Count) : 1;
        long ticks = timers.TryGetValue("tick.total", out var tt) ? Math.Max(1, tt.Count) : 1;
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        if (fd.Count > 0) sb.Append(inv, $"{frames} frames, avg frame {fd.TotalMs / frames:0.00} ms (worst {fd.MaxMs:0.0}), {ticks} ticks\n");
        sb.Append(m.Spikes.Summary()).Append('\n');
        foreach (var (name, (calls, total, max)) in timers.Where(t => t.Key != "frame.delta").OrderByDescending(t => t.Value.TotalMs).Take(count))
        {
            long per = name.StartsWith("tick.") ? ticks : frames;
            sb.Append(inv, $"  {name,-26} {total / per,7:0.000} ms/{(name.StartsWith("tick.") ? "tick " : "frame")}  avg {total / Math.Max(1, calls),7:0.000}  max {max,7:0.00}  calls {calls}\n");
        }
        return sb.ToString();
    }

    /// <summary>The spike summary and the <paramref name="count"/> most recent spikes, newest first.</summary>
    public static string Spikes(SpikeLog log, int count)
    {
        var sb = new StringBuilder(log.Summary()).Append('\n');
        foreach (var s in log.Recent(Math.Clamp(count, 0, SpikeLog.Capacity)))
            sb.Append(CultureInfo.InvariantCulture, $"  #{s.Frame,-7} {s.Describe()}\n");
        return sb.ToString();
    }
}
