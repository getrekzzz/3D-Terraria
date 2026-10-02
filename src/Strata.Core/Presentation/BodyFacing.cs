namespace Strata.Core.Presentation;

/// <summary>Which way a locomotion clip carries the feet, relative to where the hips (the legs) point.</summary>
public enum Stride
{
    /// <summary>Where the hips point: the walk, run or sprint.</summary>
    Forward,

    /// <summary>The strafe angle to the left of the hips (strafe_left).</summary>
    StrafeLeft,

    /// <summary>The strafe angle to the right of the hips (strafe_right).</summary>
    StrafeRight,

    /// <summary>Away from where the hips point: the forward gait played backwards (backpedalling).</summary>
    Backward,
}

/// <summary>
/// The third-person body rule. The chest, head and arms always face the camera's yaw (the crosshair); the hips and legs
/// turn toward the movement by at most <see cref="MaxTwist"/> (the torso twists back the rest) and play the stride
/// whose feet then travel along the movement: the forward gait straight ahead, a strafe clip (feet a fixed strafe
/// angle to the side of the hips) for sideways and diagonal moves, and the gait backwards when backing away. Standing,
/// in the air, swimming or on a rope the legs simply face the camera. Yaws are radians, + turning left (Godot's
/// convention); the game feeds the camera yaw, the movement and the frame time and turns the model and its torso.
/// </summary>
public static class BodyFacing
{
    /// <summary>The most the hips may turn from the facing (radians, 70°); the torso twists back by the same amount.</summary>
    public const float MaxTwist = 70f * MathF.PI / 180f;

    /// <summary>How far (radians, 8°) past its band a playing stride is kept, so a move along a band edge does not flicker.</summary>
    public const float Hysteresis = 8f * MathF.PI / 180f;

    /// <summary>The angle wrapped to [−π, π).</summary>
    public static float Wrap(float a)
    {
        a = (a + MathF.PI) % (2f * MathF.PI);
        if (a < 0) a += 2f * MathF.PI;
        return a - MathF.PI;
    }

    /// <summary>
    /// The stride for a move <paramref name="rel"/> radians from the facing (+ = to the left): each covers the moves
    /// nearest its travel angle, so forward ends at half the strafe angle and the strafes end halfway between their
    /// angle and straight back. The <paramref name="current"/> stride is kept within <see cref="Hysteresis"/> of its
    /// band. With no strafe clips (<paramref name="strafe"/> 0) the strafes cover ±90° and travel like the forward gait.
    /// </summary>
    public static Stride Pick(float rel, float strafe, Stride current)
    {
        rel = Wrap(rel);
        float side = MathF.Abs(rel);
        float forwardEdge = strafe * 0.5f, backEdge = (strafe + MathF.PI) * 0.5f;
        bool keep = current switch
        {
            Stride.Forward => side <= forwardEdge + Hysteresis,
            Stride.StrafeLeft => rel > forwardEdge - Hysteresis && rel <= backEdge + Hysteresis,
            Stride.StrafeRight => -rel > forwardEdge - Hysteresis && -rel <= backEdge + Hysteresis,
            _ => side > backEdge - Hysteresis,
        };
        if (keep) return current;
        if (side <= forwardEdge) return Stride.Forward;
        if (side <= backEdge) return rel > 0 ? Stride.StrafeLeft : Stride.StrafeRight;
        return Stride.Backward;
    }

    /// <summary>Where the feet travel relative to the hips for a stride (radians, + = left).</summary>
    public static float Travel(Stride stride, float strafe) => stride switch
    {
        Stride.StrafeLeft => strafe,
        Stride.StrafeRight => -strafe,
        Stride.Backward => MathF.PI,
        _ => 0f,
    };

    /// <summary>
    /// Where the legs point while moving along <paramref name="move"/> with the chest on <paramref name="facing"/>, and
    /// the stride that sends the feet along the move from there (see <see cref="Pick"/>); never more than
    /// <see cref="MaxTwist"/> from the facing.
    /// </summary>
    public static float MovingLegs(float facing, float move, float strafe, Stride current, out Stride stride)
    {
        stride = Pick(move - facing, strafe, current);
        return Limit(facing, move - Travel(stride, strafe));
    }

    /// <summary><paramref name="legs"/> turned back toward <paramref name="facing"/> so they are at most <see cref="MaxTwist"/> from it.</summary>
    public static float Limit(float facing, float legs) => facing + Math.Clamp(Wrap(legs - facing), -MaxTwist, MaxTwist);

    /// <summary>
    /// One frame of the legs turning toward <paramref name="target"/> (eased at <paramref name="rate"/> per second, the
    /// short way round), then held within <see cref="MaxTwist"/> of <paramref name="facing"/>: a fast turn of the camera
    /// drags the legs along, so the torso never has to twist further to keep the chest on the crosshair.
    /// </summary>
    public static float Turn(float legs, float target, float facing, float rate, float dt) =>
        Limit(facing, legs + Wrap(target - legs) * (1f - MathF.Exp(-rate * dt)));

    /// <summary>The torso's twist (radians, + = left) that brings the chest from the legs onto the facing.</summary>
    public static float Twist(float legs, float facing) => Wrap(facing - legs);
}
