using Strata.Core.Streaming;

namespace Strata.Core.Tests.Session;

public class FrameBudgetPlannerTests
{
    [Fact]
    public void PlayBudgetsFitInHalfAFrame()
    {
        var b = FrameBudgetPlanner.Plan(loading: false, backlog: 0, cpuFrameMs: 16.0);
        Assert.True(b.TotalMs <= 9.0, $"play budgets total {b.TotalMs} ms");
        Assert.True(b.JobsMs > 0 && b.UploadsMs > 0 && b.CollidersMs > 0 && b.ObjectsMs > 0 && b.GrassMs > 0);
    }

    [Fact]
    public void LoadingBudgetsAreGenerous()
    {
        var load = FrameBudgetPlanner.Plan(loading: true, backlog: 500, cpuFrameMs: 40.0);
        var play = FrameBudgetPlanner.Plan(loading: false, backlog: 0, cpuFrameMs: 16.0);
        Assert.True(load.JobsMs >= 10 && load.UploadsMs >= 5 && load.CollidersMs >= 3);
        Assert.True(load.TotalMs > 2 * play.TotalMs);
    }

    [Fact]
    public void BacklogRaisesJobsAndOverridesTheSlowFrameShrink()
    {
        var healthy = FrameBudgetPlanner.Plan(false, 100, 8.0);
        Assert.Equal(5.0, healthy.JobsMs);
        Assert.True(healthy.TotalMs <= 11.0);
        // A big backlog keeps streaming moving even when the CPU frame was slow.
        Assert.Equal(healthy, FrameBudgetPlanner.Plan(false, 100, 30.0));
        // A small backlog does not raise anything.
        Assert.Equal(FrameBudgetPlanner.Play, FrameBudgetPlanner.Plan(false, 10, 8.0));
    }

    [Fact]
    public void SlowFrameShrinksBudgetsButKeepsAFloor()
    {
        var normal = FrameBudgetPlanner.Plan(false, 0, 16.0);
        var slow = FrameBudgetPlanner.Plan(false, 0, 25.0);
        Assert.True(slow.TotalMs < normal.TotalMs);
        Assert.True(slow.JobsMs < normal.JobsMs && slow.UploadsMs < normal.UploadsMs && slow.CollidersMs < normal.CollidersMs);
        Assert.True(slow.ObjectsMs >= FrameBudgetPlanner.MinBudgetMs && slow.GrassMs >= FrameBudgetPlanner.MinBudgetMs);
        // Just above the target is not slow.
        Assert.Equal(normal, FrameBudgetPlanner.Plan(false, 0, 17.5));
    }
}
