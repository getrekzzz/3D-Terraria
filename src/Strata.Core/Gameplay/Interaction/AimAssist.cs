using System.Numerics;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>How strongly aim assist helps a controller (data/config/input.json, "pad.aimAssist").</summary>
public sealed class AimAssistConfig
{
    /// <summary>Half-angle (degrees) of the cone around the crosshair in which a creature is helped toward.</summary>
    public float ConeDegrees { get; init; } = 8f;

    /// <summary>How far (m) a creature may be.</summary>
    public float Range { get; init; } = 30f;

    /// <summary>The look speed multiplier with the crosshair on a creature (0.5: half speed); 1 outside the slowdown.</summary>
    public float Friction { get; init; } = 0.5f;

    /// <summary>Out to this many times the creature's angular radius the slowdown fades back to full speed.</summary>
    public float FrictionReach { get; init; } = 2.5f;

    /// <summary>Smallest angular radius (degrees) a creature is given, so a far, small one still slows the crosshair.</summary>
    public float MinRadiusDegrees { get; init; } = 1f;

    /// <summary>How quickly the view is drawn toward the creature while aiming (share of the angle per second at full input).</summary>
    public float Magnetism { get; init; } = 2f;

    /// <summary>The fastest (degrees per second) the view is ever drawn toward a creature.</summary>
    public float MaxPullDegrees { get; init; } = 25f;

    public void Validate(List<string> problems)
    {
        if (ConeDegrees is <= 0 or > 30) problems.Add("pad.aimAssist.coneDegrees must be within (0, 30]");
        if (Range is <= 0 or > 100) problems.Add("pad.aimAssist.range must be within (0, 100]");
        if (Friction is <= 0 or > 1) problems.Add("pad.aimAssist.friction must be within (0, 1]");
        if (FrictionReach < 1) problems.Add("pad.aimAssist.frictionReach must be at least 1");
        if (MinRadiusDegrees is < 0 or > 10) problems.Add("pad.aimAssist.minRadiusDegrees must be within [0, 10]");
        if (Magnetism is < 0 or > 20) problems.Add("pad.aimAssist.magnetism must be within [0, 20]");
        if (MaxPullDegrees is < 0 or > 180) problems.Add("pad.aimAssist.maxPullDegrees must be within [0, 180]");
    }
}

/// <summary>A creature aim assist helps toward: where it is, how far, and its angle off the view and angular size (radians).</summary>
public readonly record struct AimCandidate(Npc Npc, Vector3 Center, float Distance, float Angle, float AngularRadius);

/// <summary>
/// Gentle controller aim assist, as console shooters and Terraria's own gamepad lock do it, in two parts: friction
/// (the look speed drops while the crosshair is over or next to a creature, so it is easy to stay on) and magnetism
/// (while the player is actively aiming or moving, the view is drawn a little toward the creature, never faster than
/// <see cref="AimAssistConfig.MaxPullDegrees"/> and never when the sticks rest). Critters, disguised mimics, dead,
/// removed and invulnerable creatures are ignored. Angles use the camera's convention: yaw 0 looks along −Z and
/// grows turning left; pitch grows looking up. The mouse never gets it.
/// </summary>
public static class AimAssist
{
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>A creature aim assist may help toward.</summary>
    public static bool Eligible(Npc npc) =>
        !npc.Removed && npc.Alive && !npc.Disguised && !npc.Invulnerable && npc.Def.CatchItem == null;

    /// <summary>
    /// The creature inside the cone and range whose edge is closest to the crosshair (its angle off the view minus its
    /// angular radius), or null. Walks the list by index without allocating.
    /// </summary>
    public static AimCandidate? Pick(IReadOnlyList<Npc> npcs, Vector3 origin, Vector3 forward, AimAssistConfig c)
    {
        if (forward.LengthSquared() < 1e-8f) return null;
        forward = Vector3.Normalize(forward);
        float cone = c.ConeDegrees * DegToRad, minRadius = c.MinRadiusDegrees * DegToRad;
        AimCandidate? best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < npcs.Count; i++)
        {
            var npc = npcs[i];
            if (!Eligible(npc)) continue;
            var center = npc.Center;
            var to = center - origin;
            float distance = to.Length();
            if (distance < 0.3f || distance > c.Range) continue;
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(to / distance, forward), -1f, 1f));
            float radius = MathF.Max(minRadius, MathF.Atan(Radius(npc) / distance));
            if (angle > cone + radius) continue;
            float score = angle - radius;
            if (score >= bestScore) continue;
            bestScore = score;
            best = new AimCandidate(npc, center, distance, angle, radius);
        }
        return best;
    }

    /// <summary>
    /// The look speed multiplier for a creature <paramref name="angle"/> radians off the crosshair with
    /// <paramref name="angularRadius"/>: <see cref="AimAssistConfig.Friction"/> on it, easing back to 1 at
    /// <see cref="AimAssistConfig.FrictionReach"/> times its radius.
    /// </summary>
    public static float Friction(float angle, float angularRadius, AimAssistConfig c)
    {
        float inner = MathF.Max(angularRadius, c.MinRadiusDegrees * DegToRad);
        float outer = inner * MathF.Max(1.0001f, c.FrictionReach);
        if (angle <= inner) return c.Friction;
        if (angle >= outer) return 1f;
        float t = (angle - inner) / (outer - inner);
        t = t * t * (3f - 2f * t);
        return c.Friction + (1f - c.Friction) * t;
    }

    /// <summary>
    /// How much (radians of yaw and pitch) to turn the view this frame toward <paramref name="target"/>, seen from
    /// <paramref name="origin"/> with the view at <paramref name="yaw"/>/<paramref name="pitch"/>. <paramref name="input"/>
    /// is how hard the player is steering (0–1, the larger stick); nothing happens below 0.05 or once the crosshair is
    /// well on the creature (within a third of <paramref name="angularRadius"/>).
    /// </summary>
    public static (float Yaw, float Pitch) Pull(float yaw, float pitch, Vector3 origin, Vector3 target, float angularRadius,
        float input, float dt, AimAssistConfig c)
    {
        if (input < 0.05f || dt <= 0 || c.Magnetism <= 0 || c.MaxPullDegrees <= 0) return (0, 0);
        var d = target - origin;
        float errYaw = WrapAngle(YawTo(d) - yaw);
        float errPitch = PitchTo(d) - pitch;
        float error = MathF.Sqrt(errYaw * errYaw + errPitch * errPitch);
        if (error <= angularRadius / 3f) return (0, 0);
        float share = MathF.Min(1f, c.Magnetism * MathF.Min(input, 1f) * dt);
        float step = error * share, cap = c.MaxPullDegrees * DegToRad * MathF.Min(input, 1f) * dt;
        if (step > cap) share *= cap / step;
        return (errYaw * share, errPitch * share);
    }

    /// <summary>The yaw (radians, camera convention) that looks along <paramref name="d"/>.</summary>
    public static float YawTo(Vector3 d) => MathF.Atan2(-d.X, -d.Z);

    /// <summary>The pitch (radians, up positive) that looks along <paramref name="d"/>.</summary>
    public static float PitchTo(Vector3 d) => MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z));

    /// <summary>The view direction for a yaw and pitch (camera convention).</summary>
    public static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }

    /// <summary>The radius (m) a creature's box is treated as: the larger of its half width and half height.</summary>
    private static float Radius(Npc npc) => MathF.Max(MathF.Max(npc.HalfExtents.X, npc.HalfExtents.Z), npc.HalfExtents.Y);

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }
}
