using System.Numerics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Core.Gameplay.Entities;

/// <summary>The last creature a melee strike connected with: the blade angle (curve degrees; NaN for a thrust) and how far from the pivot.</summary>
public readonly record struct MeleeHit(long NpcId, float BladeDeg, float Distance);

/// <summary>
/// The hit tests of a melee strike, sampled from the same <see cref="SwingCurve"/> and <see cref="SwingPlane"/> the
/// trail and the first-person arc draw. A swing tests the blade swept between the curve's angles at the last tick and
/// this one, from the clip's strike start to its strike end plus the follow-through (so nothing connects during the
/// wind-up); a thrust connects along the aim only while it is extended at least <see cref="ThrustLive"/>, as far as
/// it has extended.
/// </summary>
public sealed partial class MeleeSystem
{
    /// <summary>How far off the aim line a thrust still connects (m): generous, since aiming a short blade at a circling
    /// flyer in 3D is much harder than in the reference game's side view.</summary>
    public const float ThrustTolerance = 0.7f;

    /// <summary>The extension (0–1) from which a thrust connects.</summary>
    public const float ThrustLive = 0.6f;

    /// <summary>Horizontal distance (m) inside which a swing always connects: a creature in the player's face.</summary>
    public const float PointBlank = 0.6f;

    /// <summary>
    /// The highest blade angle (degrees after the arc scaling; 90 is straight up) that deals damage: a little forward
    /// of vertical, so the blade raised behind the head never hits creatures behind the player.
    /// </summary>
    public const float HighestDamagingDeg = 80f;

    /// <summary>The most recent hit of any swing or thrust (probes and tests); null before the first.</summary>
    public MeleeHit? LastHit { get; private set; }

    private float _hitDeg;
    private ClipTiming? _bladeClip, _thrustClip;
    private SwingCurve? _blade, _extension;

    /// <summary>The swing clip's blade curve (rebuilt only when the timing is reloaded).</summary>
    private SwingCurve BladeCurve(ClipTiming clip) => clip == _bladeClip ? _blade! : _blade = SwingCurve.Blade(_bladeClip = clip);

    private SwingCurve ExtensionCurve(ClipTiming clip) => clip == _thrustClip ? _extension! : _extension = SwingCurve.Extension(_thrustClip = clip);
    private Vector3 _hitPivot;

    /// <summary>The plane a player's swing sweeps (pivot, reach and arc of the swung stack).</summary>
    public static SwingPlane PlaneFor(Vector3 center, float yaw, MeleeSwingInfo swing) =>
        new(center, yaw, SwingPlane.SwingReach(swing.Length, swing.Size), swing.Arc);

    private void Strike(Swing s, UseTiming timing, float tPrev, float t)
    {
        if (s.Item.Has(ItemAction.Thrust))
        {
            float ext = ExtensionCurve(timing.Clip(UseTiming.Thrust)).Evaluate(t);
            if (ext < ThrustLive) return;
            float reach = SwingPlane.ThrustReach(s.Item.ThrustParams!.Length, Prefixes.Size(s.Stack)) * ext;
            var origin = s.Player.Center + new Vector3(0, 0.3f, 0);
            var dir = new Vector3(MathF.Sin(s.Yaw) * MathF.Cos(s.Pitch), MathF.Sin(s.Pitch), MathF.Cos(s.Yaw) * MathF.Cos(s.Pitch));
            _hitDeg = float.NaN;
            _hitPivot = origin;
            HitCreatures(s, (min, max) => ThrustConnects(origin, dir, reach, min, max));
            return;
        }
        var clip = timing.Clip(UseTiming.Swing);
        float start = clip.StrikeStart ?? 0f, end = MathF.Min(1f, (clip.StrikeEnd ?? 1f) + UseTiming.FollowThrough);
        if (t < start || tPrev > end) return;
        var curve = BladeCurve(clip);
        var swing = s.Item.Swing!;
        // Only the part of the arc in front of the player damages: the curve starts raised behind the head (scaled
        // further back by wide arcs), so the swept angles are capped short of straight up. The trail still draws the
        // whole curve.
        float cap = HighestDamagingDeg * 150f / swing.Arc;
        float from = MathF.Min(cap, float.IsNaN(s.LastDeg) ? curve.Evaluate(MathF.Max(tPrev, start)) : s.LastDeg);
        float to = MathF.Min(cap, curve.Evaluate(MathF.Min(t, end)));
        s.LastDeg = to;
        var plane = PlaneFor(s.Player.Center, s.Yaw, new MeleeSwingInfo(swing.Arc, swing.Length, Prefixes.Size(s.Stack)));
        var center = s.Player.Center;
        _hitPivot = plane.Pivot;
        HitCreatures(s, (min, max) =>
        {
            if (plane.Sweeps(from, to, min, max, out _hitDeg)) return true;
            // Point blank: a creature in the player's face is struck wherever the blade is.
            var near = Vector3.Clamp(center, min, max) - center;
            _hitDeg = to;
            return new Vector2(near.X, near.Z).Length() < PointBlank && MathF.Abs(near.Y) <= VerticalReach;
        });
    }

    /// <summary>A thrust connects with a box ahead of <paramref name="origin"/>, no further than the stab has reached, close to the aim line.</summary>
    private static bool ThrustConnects(Vector3 origin, Vector3 dir, float reach, Vector3 min, Vector3 max)
    {
        var center = (min + max) * 0.5f;
        var half = (max - min) * 0.5f;
        var to = center - origin;
        float radius = MathF.Max(half.X, half.Y);
        float along = Vector3.Dot(to, dir);
        if (along < 0 || along > reach + radius) return false;
        return (to - dir * along).Length() <= radius + ThrustTolerance;
    }
}

/// <summary>What a swing's plane needs from the swung item: its arc (degrees), blade length (cells) and size prefix.</summary>
public readonly record struct MeleeSwingInfo(float Arc, float Length, float Size);
