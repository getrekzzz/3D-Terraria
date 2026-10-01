using Strata.Core.Commands;
using Strata.Core.Diagnostics;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Diagnostics;

public class SpikeLogTests
{
    private static Metrics Tracking() => new() { Enabled = true, TrackFrames = true };

    [Fact]
    public void FramesUnderTheThresholdAreCountedButNotLogged()
    {
        var m = Tracking();
        m.Record("gd.colliders", 5);
        m.EndFrame(16.7);
        m.EndFrame(32.9);

        var log = m.Spikes;
        Assert.Equal(2, log.Frames);
        Assert.Equal(0, log.Over33);
        Assert.Empty(log.Recent());
        Assert.Equal(32.9, log.WorstMs, 6);
    }

    [Fact]
    public void ASpikeKeepsItsTopFiveTimersSortedByMs()
    {
        var m = Tracking();
        m.Record("gd.grass.create", 2);
        m.Record("gd.colliders.build", 6);
        m.Record("gd.colliders.build", 6); // two calls in the frame add up to 12
        m.Record("tick.autosave", 40.1);
        m.Record("gd.objects.rebuild", 3);
        m.Record("gd.npcs.create", 1);
        m.Record("gd.hud", 0.5);
        m.Record("frame.delta", 72); // the frame's own length is not work done in it
        m.EndFrame(72);

        var log = m.Spikes;
        Assert.Equal(1, log.Over33);
        Assert.Equal(1, log.Over50);
        Assert.Equal(0, log.Over100);
        var spike = Assert.Single(log.Recent());
        Assert.Equal(1, spike.Frame);
        Assert.Equal(72, spike.Ms);
        Assert.Equal(new[] { "tick.autosave", "gd.colliders.build", "gd.objects.rebuild", "gd.grass.create", "gd.npcs.create" }, spike.Top.Select(t => t.Timer));
        Assert.Equal(new[] { 40.1, 12, 3, 2, 1 }, spike.Top.Select(t => t.Ms));
        Assert.StartsWith("72.0 ms: tick.autosave 40.1, gd.colliders.build 12.0", spike.Describe());
        Assert.Equal(2, log.PeakCalls("gd.colliders.build"));
    }

    [Fact]
    public void ASpikeListsCausesNotTheTimersEnclosingThem()
    {
        var m = Tracking();
        m.Record("gd.world_runtime", 37); // encloses grass and colliders
        m.Record("tick.total", 20); // encloses tick.liquids
        m.Record("tick.liquids", 19.6);
        m.Record("gd.grass", 21.6);
        m.Record("gd.grass.create", 21.3); // explains gd.grass
        m.Record("gd.grassy", 0.2); // not a child of gd.grass
        m.Record("gd.npcs", 31.5);
        m.Record("gd.npcs.create", 24.4); // leaves 7 ms of gd.npcs unexplained: both stay
        m.EndFrame(60);

        var spike = Assert.Single(m.Spikes.Recent());
        Assert.Equal(new[] { "gd.npcs", "gd.npcs.create", "gd.grass.create", "tick.liquids", "gd.grassy" }, spike.Top.Select(t => t.Timer));
        Assert.Equal(37, m.Timer("gd.world_runtime").MaxMs); // still in perf report
    }

    [Fact]
    public void FrameTotalsStartOverEachFrame()
    {
        var m = Tracking();
        m.Record("stream.unload_flush", 30);
        m.EndFrame(20); // not a spike: its 30 ms of timers must not leak into the next frame
        m.Record("gd.dynres.change", 4);
        m.EndFrame(45);

        var spike = Assert.Single(m.Spikes.Recent());
        Assert.Equal(2, spike.Frame);
        var only = Assert.Single(spike.Top);
        Assert.Equal(("gd.dynres.change", 4.0), only);
    }

    [Fact]
    public void TheRingKeepsTheLast64SpikesNewestFirst()
    {
        var m = Tracking();
        for (int i = 1; i <= 70; i++)
        {
            m.Record("tick.total", i);
            m.EndFrame(40 + i);
        }

        var recent = m.Spikes.Recent();
        Assert.Equal(SpikeLog.Capacity, recent.Count);
        Assert.Equal(70, recent[0].Frame);
        Assert.Equal(7, recent[^1].Frame);
        Assert.Equal(new long[] { 70, 69, 68 }, m.Spikes.Recent(3).Select(s => s.Frame));
        Assert.Equal(70, m.Spikes.Over33);
        Assert.Equal(110, m.Spikes.WorstMs);
    }

    [Fact]
    public void NothingIsTrackedWhileFrameTrackingIsOff()
    {
        var m = new Metrics { Enabled = true };
        m.Record("gd.colliders.build", 80);
        m.EndFrame(90);
        Assert.Equal(0, m.Spikes.Frames);
        Assert.Equal(1, m.Timer("gd.colliders.build").Count); // the ordinary timers still run

        m.TrackFrames = true;
        m.EndFrame(90);
        Assert.Empty(Assert.Single(m.Spikes.Recent()).Top); // the 80 ms recorded before tracking is not blamed on it
    }

    [Fact]
    public void AFrameTrackingStoppedInIsNotBlamedOnTheNextSession()
    {
        var m = Tracking();
        m.Record("gd.colliders.build", 80); // the frame `perf stop` (or closing F3) ran in never closes
        m.TrackFrames = false;
        m.TrackFrames = true;
        m.EndFrame(90);
        Assert.Empty(Assert.Single(m.Spikes.Recent()).Top);

        m.Record("gd.objects.rebuild", 60); // the frame that loaded a world has no start: FrameProbe discards it
        m.Spikes.DiscardFrame();
        m.EndFrame(70);
        Assert.Equal(2, m.Spikes.Frames);
        Assert.Empty(m.Spikes.Recent(1)[0].Top);
    }

    [Fact]
    public void ClosingAnOrdinaryFrameAllocatesNothing()
    {
        var m = Tracking();
        void Frame()
        {
            for (int i = 0; i < 50; i++)
            {
                using (m.Time("gd.colliders")) m.Record("gd.colliders.build", 0.1);
                m.Record("tick.total", 0.2);
            }
            m.EndFrame(16.7);
        }
        Frame(); // timers and their frame slots are created once
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(101, m.Spikes.Frames);
        Assert.Equal(50, m.Spikes.PeakCalls("gd.colliders.build"));
    }

    [Fact]
    public void PerfStartCountsAsAReset()
    {
        var m = Tracking();
        int before = m.Resets;
        m.Reset();
        Assert.Equal(before + 1, m.Resets); // the F3 overlay leaves a session started while it was open running
    }

    /// <summary>A percentile is the upper edge of its 0.5 ms histogram step, never above the worst frame.</summary>
    [Fact]
    public void PercentilesComeFromTheFrameTimeHistogram()
    {
        var m = Tracking();
        for (int i = 0; i < 98; i++) m.EndFrame(15.9);
        m.EndFrame(59.9);
        m.EndFrame(250);

        var log = m.Spikes;
        Assert.Equal(16, log.Percentile(0.5), 6);
        Assert.Equal(16, log.Percentile(0.98), 6);
        Assert.Equal(60, log.Percentile(0.99), 6);
        Assert.Equal(250, log.Percentile(1), 6);
        Assert.Contains("p99 60.0 max 250.0 ms", log.Summary());
    }

    /// <summary>Software rendering runs at seconds per frame: percentiles stay meaningful there (10 ms steps to 10 s).</summary>
    [Fact]
    public void SlowFramesKeepTheirPercentiles()
    {
        var m = Tracking();
        for (int i = 0; i < 90; i++) m.EndFrame(1505);
        for (int i = 0; i < 9; i++) m.EndFrame(2995);
        m.EndFrame(18_000);

        var log = m.Spikes;
        Assert.Equal(1510, log.Percentile(0.5), 6);
        Assert.Equal(3000, log.Percentile(0.99), 6);
        Assert.Equal(18_000, log.Percentile(1), 6);
        Assert.Equal(100, log.Over100);
    }

    [Fact]
    public void TheSummarySaysWhenFramesExcludeTheDraw()
    {
        var m = Tracking();
        m.EndFrame(5);
        Assert.Contains("of 1 frames ·", m.Spikes.Summary());
        m.Spikes.MainThreadOnly = true; // software rendering: the engine measures main-thread work only
        m.Reset();
        Assert.Contains("of 0 frames (main thread, draw excluded) ·", m.Spikes.Summary());
    }

    [Fact]
    public void ASpikeRecordsTheCollectionsThatRanDuringIt()
    {
        var m = Tracking();
        m.EndFrame(16);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        m.EndFrame(80);

        var spike = Assert.Single(m.Spikes.Recent());
        Assert.True(spike.Gen2 >= 1);
        Assert.Contains("gen2", spike.Describe());
        Assert.True(m.Spikes.Collections.Gen2 >= 1);
        Assert.True(m.Spikes.GcPauseMs >= spike.GcPauseMs);
    }

    [Fact]
    public void ConcurrentRecordsAllLandInTheFrame()
    {
        var m = Tracking();
        Parallel.For(0, 4000, _ => m.Record("job.mesh", 0.25));
        m.EndFrame(1000);
        var top = Assert.Single(Assert.Single(m.Spikes.Recent()).Top);
        Assert.Equal(1000, top.Ms, 6);
        Assert.Equal(4000, m.Spikes.PeakCalls("job.mesh"));
    }

    [Fact]
    public void PerfStartResetsTheCountersAndPerfProbesReadThem()
    {
        using var g = TestGame.Create();
        var r = BuiltinCommands.CreateRegistry();
        var ctx = new CommandContext(g.World, new HeadlessCommandHost()) { Registry = r };
        var m = Metrics.Frame;
        try
        {
            Assert.True(r.Execute(ctx, "perf start").Ok);
            Assert.True(m.Enabled && m.TrackFrames);
            m.Record("test.spike_probe", 3);
            m.Record("test.spike_probe", 9.5);
            m.EndFrame(70);
            Assert.Equal(9.5, ctx.ReadProbe("perf.max.test.spike_probe"), 6);
            Assert.Equal(6.25, ctx.ReadProbe("perf.avg.test.spike_probe"), 6);
            Assert.Equal(2, ctx.ReadProbe("perf.calls.test.spike_probe"));
            Assert.Equal(2, ctx.ReadProbe("perf.max_calls.test.spike_probe"));
            Assert.Equal(0, ctx.ReadProbe("perf.max.test.never_ran"));
            Assert.Equal(1, ctx.ReadProbe("perf.frames_over_33"));
            Assert.Equal(1, ctx.ReadProbe("perf.frames_over_50"));
            Assert.Equal(70, ctx.ReadProbe("perf.worst_ms"), 6);
            Assert.True(ctx.ReadProbe("perf.gc2") >= 0);
            Assert.True(r.Execute(ctx, "assert perf.max.test.spike_probe > 9").Ok);
            var spikes = r.Execute(ctx, "perf spikes 5");
            Assert.True(spikes.Ok);
            Assert.Contains("test.spike_probe 12.5", spikes.Output); // other tests may tick (and time) the world meanwhile
            var report = r.Execute(ctx, "perf report 5");
            Assert.Contains("spikes >=33 ms 1 (>=50 1, >=100 0) of 1 frames", report.Output);
            Assert.Contains("GC ", report.Output);

            Assert.True(r.Execute(ctx, "perf start").Ok);
            Assert.Equal(0, ctx.ReadProbe("perf.frames"));
            Assert.Equal(0, ctx.ReadProbe("perf.frames_over_33"));
            Assert.Equal(0, ctx.ReadProbe("perf.worst_ms"));
            Assert.Equal(0, ctx.ReadProbe("perf.calls.test.spike_probe"));
            Assert.Equal(0, ctx.ReadProbe("perf.max_calls.test.spike_probe"));
            Assert.Empty(m.Spikes.Recent());
        }
        finally
        {
            Assert.True(r.Execute(ctx, "perf stop").Ok);
        }
        Assert.False(m.Enabled || m.TrackFrames);
    }
}
