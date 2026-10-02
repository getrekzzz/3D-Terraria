using System.Numerics;
using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class SpringSwayTests
{
    private static SpringSway Run(Func<float, Vector3> anchorAt, float seconds, float dt = 1f / 60f)
    {
        var s = new SpringSway();
        for (float t = 0f; t < seconds; t += dt) s.Step(anchorAt(t), dt);
        return s;
    }

    [Fact]
    public void AtRestNothingMoves()
    {
        Assert.Equal(Vector3.Zero, Run(_ => new Vector3(3, 1, 2), 2f).Offset);
    }

    [Fact]
    public void StartingToRunPushesTheHairBackwards()
    {
        // Accelerating along +X: loose parts trail toward -X.
        var s = Run(t => new Vector3(t < 0.5f ? 0f : 4f * (t - 0.5f) * (t - 0.5f), 0, 0), 0.6f);
        Assert.True(s.Offset.X < -0.01f, $"offset {s.Offset}");
    }

    [Fact]
    public void SteadyMotionSettlesBackToRest()
    {
        var s = Run(t => new Vector3(5f * t, 0, 0), 4f);
        Assert.True(s.Offset.Length() < 0.005f, $"offset {s.Offset}");
    }

    [Fact]
    public void OffsetsAreBoundedAndTeleportsReset()
    {
        var s = Run(t => new Vector3(MathF.Sin(t * 40f) * 2f, 0, 0), 3f);
        Assert.True(s.Offset.Length() <= SpringSway.MaxOffset + 1e-5f);
        s.Step(new Vector3(500, 0, 0), 1f / 60f);
        Assert.Equal(Vector3.Zero, s.Offset);
    }
}
