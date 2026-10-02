using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class ResolutionGovernorTests
{
    private const double Vsync = 1000.0 / 60.0;

    /// <summary>Runs <paramref name="seconds"/> of frames; returns the times (s) of every change.</summary>
    private static List<double> Run(ResolutionGovernor g, double seconds, Func<int, (double Frame, double Gpu)> frameAt, List<float>? scales = null)
    {
        var changes = new List<double>();
        double t = 0;
        for (int i = 0; t < seconds; i++)
        {
            var (frame, gpu) = frameAt(i);
            t += frame / 1000.0;
            if (g.Feed(frame, gpu))
            {
                changes.Add(t);
                scales?.Add(g.Scale);
            }
        }
        return changes;
    }

    [Fact]
    public void NoOscillationNearThresholds()
    {
        var g = new ResolutionGovernor(1f);
        // Whole windows alternate between a comfortable 11 ms and a borderline 14 ms (frames just over the target).
        var changes = Run(g, 120, i => (i / ResolutionGovernor.Window) % 2 == 0 ? (Vsync, 11.0) : (18.0, 14.0));
        for (double start = 0; start < 120; start += 1)
            Assert.True(changes.Count(c => c >= start && c < start + 10) <= 1, $"more than one change in 10 s from {start}");
    }

    [Fact]
    public void SustainedOverloadStepsDown()
    {
        var g = new ResolutionGovernor(1f);
        var scales = new List<float>();
        var changes = Run(g, 40, _ => (25.0, 24.0), scales);
        Assert.Equal(new[] { 0.9f, 0.8f, 0.7f, 0.6f }, scales);
        Assert.Equal(0.6f, g.Scale);
        for (int i = 1; i < changes.Count; i++) Assert.True(changes[i] - changes[i - 1] >= ResolutionGovernor.MinGapS - 0.05);
    }

    [Fact]
    public void RecoveryStepsUpAfterCooldown()
    {
        var g = new ResolutionGovernor(1f);
        var down = Run(g, 30, _ => (25.0, 24.0));
        Assert.Equal(0.6f, g.Scale);
        double lastDown = down[^1];
        var scales = new List<float>();
        var up = Run(g, 60, _ => (Vsync, 5.0), scales);
        Assert.Equal(new[] { 0.7f, 0.8f, 0.9f, 1.0f }, scales);
        Assert.True(up[0] + 30 - lastDown >= ResolutionGovernor.UpCooldownS - 0.05); // the second run started at t = 30 s
    }

    [Fact]
    public void NoStepUpWhenTheNextLevelWouldBeTooSlow()
    {
        var g = new ResolutionGovernor(1f);
        Run(g, 10, _ => (25.0, 24.0));
        float scale = g.Scale;
        // 10 ms now, but 10 × (next/current)² is over the 12 ms budget at the next level up.
        Run(g, 60, _ => (Vsync, 10.0));
        Assert.Equal(scale, g.Scale);
    }

    [Fact]
    public void HitchFramesIgnored()
    {
        var g = new ResolutionGovernor(1f);
        // Every 20th frame is a 200 ms CPU hitch with the GPU also reporting a long frame: fewer pixels would not help.
        var changes = Run(g, 60, i => i % 20 == 19 ? (200.0, 30.0) : (Vsync, 9.0));
        Assert.Empty(changes);
        Assert.Equal(1f, g.Scale);
    }

    [Fact]
    public void CappedAtThePresetScale()
    {
        var g = new ResolutionGovernor(0.75f);
        Assert.Equal(0.75f, g.Scale);
        var scales = new List<float>();
        Run(g, 30, _ => (25.0, 24.0), scales);
        Assert.Equal(new[] { 0.7f, 0.6f }, scales);
        Assert.Equal(2, g.Changes);
    }

    [Fact]
    public void NeverStepsDownWithoutGpuTiming()
    {
        var g = new ResolutionGovernor(1f);
        Assert.Empty(Run(g, 30, _ => (25.0, 0.0)));
        Assert.Equal(1f, g.Scale);
    }
}
