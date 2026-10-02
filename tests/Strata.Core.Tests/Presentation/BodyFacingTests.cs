using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class BodyFacingTests
{
    private const float Deg = MathF.PI / 180f;
    private const float Strafe = 45f * Deg; // the generated strafe clips (HumanoidAnimator.StrafeAngle)

    private static float AngleBetween(float a, float b) => MathF.Abs(BodyFacing.Wrap(a - b));

    [Theory]
    [InlineData(0f, Stride.Forward)]
    [InlineData(45f, Stride.StrafeLeft)]
    [InlineData(90f, Stride.StrafeLeft)]
    [InlineData(-45f, Stride.StrafeRight)]
    [InlineData(-90f, Stride.StrafeRight)]
    [InlineData(135f, Stride.Backward)]
    [InlineData(-135f, Stride.Backward)]
    [InlineData(180f, Stride.Backward)]
    public void EveryKeyDirectionSendsTheFeetAlongTheMoveWithTheChestOnTarget(float relDegrees, Stride expected)
    {
        foreach (float facing in new[] { 0f, 2.5f, -3.1f })
        {
            float move = facing + relDegrees * Deg;
            float legs = BodyFacing.MovingLegs(facing, move, Strafe, Stride.Forward, out var stride);
            Assert.Equal(expected, stride);
            // The clip carries the feet exactly along the move, and the torso twists the chest back onto the facing.
            Assert.True(AngleBetween(legs + BodyFacing.Travel(stride, Strafe), move) < 1e-4f, $"feet off the move at {relDegrees}°");
            Assert.True(AngleBetween(legs + BodyFacing.Twist(legs, facing), facing) < 1e-4f);
            Assert.True(MathF.Abs(BodyFacing.Twist(legs, facing)) <= 45f * Deg + 1e-4f, $"twist over 45° at {relDegrees}°");
        }
    }

    [Fact]
    public void ForwardDiagonalsStrafeWithSquareHips()
    {
        // W+A: the strafe clip itself runs 45° to the left, so the hips stay on the crosshair.
        float legs = BodyFacing.MovingLegs(1f, 1f + 45f * Deg, Strafe, Stride.Forward, out var stride);
        Assert.Equal(Stride.StrafeLeft, stride);
        Assert.Equal(1f, legs, 4);
    }

    [Fact]
    public void NoMoveDirectionTwistsPastTheLimitAndFreshPicksNeverSlideTheFeet()
    {
        for (int d = -180; d < 180; d++)
        {
            float move = d * Deg;
            float legs = BodyFacing.MovingLegs(0f, move, Strafe, Stride.Forward, out var stride);
            Assert.True(MathF.Abs(BodyFacing.Twist(legs, 0f)) <= BodyFacing.MaxTwist + 1e-4f, $"twist past the limit at {d}°");
            if (stride == Stride.Forward && MathF.Abs(d) > 22.5f + 8f) Assert.Fail($"forward kept at {d}°");
            Assert.True(AngleBetween(legs + BodyFacing.Travel(stride, Strafe), move) < 1e-4f, $"feet slide at {d}°");
        }
    }

    [Fact]
    public void APlayingStrideHoldsAcrossItsBandEdge()
    {
        // 25° is past forward's edge (22.5°) but within the hysteresis: a forward run keeps running forward…
        Assert.Equal(Stride.Forward, BodyFacing.Pick(25f * Deg, Strafe, Stride.Forward));
        // …a strafe keeps strafing, and a fresh choice there is the strafe.
        Assert.Equal(Stride.StrafeLeft, BodyFacing.Pick(20f * Deg, Strafe, Stride.StrafeLeft));
        Assert.Equal(Stride.StrafeLeft, BodyFacing.Pick(25f * Deg, Strafe, Stride.Backward));
        Assert.Equal(Stride.StrafeLeft, BodyFacing.Pick(35f * Deg, Strafe, Stride.Forward));
        // Past the hysteresis the stride changes; reversing the direction always backpedals.
        Assert.Equal(Stride.Backward, BodyFacing.Pick(125f * Deg, Strafe, Stride.StrafeLeft));
        Assert.Equal(Stride.Backward, BodyFacing.Pick(MathF.PI, Strafe, Stride.Forward));
        Assert.Equal(Stride.StrafeRight, BodyFacing.Pick(-100f * Deg, Strafe, Stride.Backward));
    }

    [Fact]
    public void WithoutStrafeClipsSidewaysMovesTurnTheLegsAsFarAsTheLimitAllows()
    {
        float legs = BodyFacing.MovingLegs(0f, 90f * Deg, 0f, Stride.Forward, out var stride);
        Assert.Equal(Stride.StrafeLeft, stride);
        Assert.Equal(0f, BodyFacing.Travel(stride, 0f));
        Assert.Equal(BodyFacing.MaxTwist, legs, 4);
        BodyFacing.MovingLegs(0f, 100f * Deg, 0f, Stride.Forward, out stride);
        Assert.Equal(Stride.Backward, stride);
    }

    [Fact]
    public void ACameraWhipDragsTheLegsSoTheChestStaysOnTarget()
    {
        // Standing facing 0, the camera turns 180° in one frame: the legs cannot ease that far, but they are pulled to
        // within the twist limit at once, so the chest (legs + twist) is exactly on the new facing.
        float facing = MathF.PI - 0.01f;
        float legs = BodyFacing.Turn(0f, facing, facing, 12f, 1f / 60f);
        Assert.True(AngleBetween(legs, facing) <= BodyFacing.MaxTwist + 1e-4f);
        Assert.True(AngleBetween(legs + BodyFacing.Twist(legs, facing), facing) < 1e-4f);
    }

    [Fact]
    public void StandingLegsEaseOntoTheFacing()
    {
        float legs = 0f, facing = 0.6f;
        legs = BodyFacing.Turn(legs, facing, facing, 12f, 1f / 60f);
        Assert.True(legs > 0f && legs < 0.6f);
        for (int i = 0; i < 60; i++) legs = BodyFacing.Turn(legs, facing, facing, 12f, 1f / 60f);
        Assert.Equal(0.6f, legs, 3);
        // The short way round across ±π.
        float across = BodyFacing.Turn(3.1f, -3.1f, -3.1f, 12f, 1f / 60f);
        Assert.True(AngleBetween(across, 3.1f) < 0.1f && AngleBetween(across, -3.1f) < 0.1f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3f * MathF.PI, -MathF.PI)]
    [InlineData(-3.5f * MathF.PI, 0.5f * MathF.PI)]
    [InlineData(1.5f * MathF.PI, -0.5f * MathF.PI)]
    public void WrapKeepsAnglesWithinHalfATurn(float a, float expected) => Assert.Equal(expected, BodyFacing.Wrap(a), 4);
}
