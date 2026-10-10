using Strata.Core.Diagnostics;

namespace Strata.Core.Commands;

/// <summary>
/// Frame-profiler probes (read <see cref="Metrics.Frame"/>; zero until <c>perf start</c> or F3 turns it on): slow-frame
/// counts, the worst frame, frame-time p99, garbage collections, and per-timer <c>perf.max.&lt;timer&gt;</c> (longest
/// call, ms), <c>perf.avg.&lt;timer&gt;</c> (ms per call), <c>perf.calls.&lt;timer&gt;</c> and
/// <c>perf.max_calls.&lt;timer&gt;</c> (most calls in one frame). Unknown timers read 0: they simply have not run.
/// </summary>
[ProbeSource]
internal static class PerfProbes
{
    public static void Register(CommandContext ctx)
    {
        var m = Metrics.Frame;
        var log = m.Spikes;
        ctx.AddProbe("perf.frames", () => log.Frames);
        ctx.AddProbe("perf.frames_over_33", () => log.Over33);
        ctx.AddProbe("perf.frames_over_50", () => log.Over50);
        ctx.AddProbe("perf.frames_over_100", () => log.Over100);
        ctx.AddProbe("perf.worst_ms", () => log.WorstMs);
        ctx.AddProbe("perf.p99_ms", () => log.Percentile(0.99));
        ctx.AddProbe("perf.gc0", () => log.Collections.Gen0);
        ctx.AddProbe("perf.gc1", () => log.Collections.Gen1);
        ctx.AddProbe("perf.gc2", () => log.Collections.Gen2);
        ctx.AddProbe("perf.gc_pause_ms", () => log.GcPauseMs);
        ctx.AddPrefixProbe("perf.max.", timer => m.Timer(timer).MaxMs);
        ctx.AddPrefixProbe("perf.avg.", timer => m.Timer(timer) is { Count: > 0 } t ? t.TotalMs / t.Count : 0);
        ctx.AddPrefixProbe("perf.calls.", timer => m.Timer(timer).Count);
        ctx.AddPrefixProbe("perf.max_calls.", timer => log.PeakCalls(timer));
    }
}
