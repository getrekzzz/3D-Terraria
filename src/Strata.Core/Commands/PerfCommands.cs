using System.Globalization;
using System.Text;
using Strata.Core.Diagnostics;

namespace Strata.Core.Commands;

/// <summary>The frame profiler: <c>perf start</c> clears and enables it, <c>perf report</c> prints where the time went.</summary>
public static class PerfCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "perf", Usage = "perf <start|stop|report [count]>", Help = "Profiles simulation ticks and engine frames.",
            Run = (ctx, a) =>
            {
                var m = Metrics.Frame;
                switch (a.Str(0, "action"))
                {
                    case "start":
                        m.Reset();
                        m.Enabled = true;
                        return CommandResult.Success("Profiling.");
                    case "stop":
                        m.Enabled = false;
                        return CommandResult.Success("Profiling stopped.");
                    case "report":
                        return CommandResult.Success(Report(m, a.OptInt(1, 30)));
                    default:
                        throw new CommandException("expected start, stop or report");
                }
            },
        });
    }

    /// <summary>Per-frame averages (ms per rendered frame), average and worst per call, sorted by total cost.</summary>
    public static string Report(Metrics m, int count)
    {
        var timers = m.SnapshotTimers();
        long frames = timers.TryGetValue("frame.delta", out var fd) ? Math.Max(1, fd.Count) : 1;
        long ticks = timers.TryGetValue("tick.total", out var tt) ? Math.Max(1, tt.Count) : 1;
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        if (fd.Count > 0) sb.Append(inv, $"{frames} frames, avg frame {fd.TotalMs / frames:0.00} ms (worst {fd.MaxMs:0.0}), {ticks} ticks\n");
        foreach (var (name, (calls, total, max)) in timers.Where(t => t.Key != "frame.delta").OrderByDescending(t => t.Value.TotalMs).Take(count))
        {
            long per = name.StartsWith("tick.") ? ticks : frames;
            sb.Append(inv, $"  {name,-26} {total / per,7:0.000} ms/{(name.StartsWith("tick.") ? "tick " : "frame")}  avg {total / Math.Max(1, calls),7:0.000}  max {max,7:0.00}  calls {calls}\n");
        }
        return sb.ToString();
    }
}
