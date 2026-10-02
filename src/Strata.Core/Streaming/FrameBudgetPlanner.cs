namespace Strata.Core.Streaming;

/// <summary>Main-thread time budgets (milliseconds) for one frame of streaming and presentation work.</summary>
public readonly record struct FrameBudgets(double JobsMs, double UploadsMs, double CollidersMs, double ObjectsMs, double GrassMs)
{
    public double TotalMs => JobsMs + UploadsMs + CollidersMs + ObjectsMs + GrassMs;
}

/// <summary>
/// Picks the per-frame budgets. While the world loads (player frozen behind the loading screen) the budgets are big so
/// the world appears quickly; during play they stay well under half a 60 Hz frame and shrink after a slow frame, so
/// streaming, uploads and colliders never stack up into a visible hitch.
/// </summary>
public static class FrameBudgetPlanner
{
    public static readonly FrameBudgets Loading = new(12.0, 6.0, 4.0, 3.0, 2.0);
    public static readonly FrameBudgets Play = new(3.0, 2.0, 1.5, 1.0, 0.8);

    /// <summary>Job completions waiting above this count get a larger job budget (on healthy frames).</summary>
    public const int BacklogThreshold = 64;

    public const double BacklogJobsMs = 5.0;
    public const double SlowFrameFactor = 1.1;
    public const double SlowFrameScale = 0.6;
    public const double MinBudgetMs = 0.5;

    public static FrameBudgets Plan(bool loading, int backlog, double lastFrameMs, double targetMs = 16.7)
    {
        if (loading) return Loading;
        var b = Play;
        if (lastFrameMs > targetMs * SlowFrameFactor)
            return new FrameBudgets(Shrink(b.JobsMs), Shrink(b.UploadsMs), Shrink(b.CollidersMs), Shrink(b.ObjectsMs), Shrink(b.GrassMs));
        return backlog > BacklogThreshold ? b with { JobsMs = BacklogJobsMs } : b;
    }

    private static double Shrink(double ms) => Math.Max(MinBudgetMs, ms * SlowFrameScale);
}
