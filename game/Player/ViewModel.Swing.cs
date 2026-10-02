using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.Player;

/// <summary>
/// First-person melee from the shared swing curve (P3-04): the blade angle of <see cref="SwingCurve.Blade"/> at the
/// use's phase is turned into the same direction in camera space as <see cref="SwingPlane"/> gives in the world (cos θ
/// ahead plus sin θ up, the plane leaning <see cref="SwingPlane.PlaneTilt"/> toward the right), so the arc on screen
/// is the gameplay arc: raised high in front, over and down, low through the follow-through. A
/// thrust lunges by its <see cref="SwingCurve.Extension"/>, so the point is out exactly while the stab can hit.
/// </summary>
public sealed partial class ViewModel
{
    /// <summary>How far the hand travels with the blade (m along the blade's direction), and its drift left across the view over the strike.</summary>
    private const float HandReach = 0.1f, HandAcross = 0.14f;

    /// <summary>Twist of the blade about its own axis so its flat catches the eye rather than showing only the edge.</summary>
    private static readonly float FlatTwist = Mathf.DegToRad(35f);

    private SwingCurve? _extension;
    private ClipTiming? _thrustClip;

    /// <summary>The blade direction in camera space at curve angle <paramref name="deg"/> (as SwingPlane.Direction, ahead = −Z).</summary>
    public static Vector3 BladeDirection(float deg, float arcScale = 1f)
    {
        float a = Mathf.DegToRad(deg * arcScale), tilt = Mathf.DegToRad(SwingPlane.PlaneTilt);
        var up = new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), 0f);
        return new Vector3(0, 0, -1) * Mathf.Cos(a) + up * Mathf.Sin(a);
    }

    private Pose BladePose(float t)
    {
        var blade = _game.ItemUse.Timing.BladeCurve;
        float arc = (_item != null ? _game.Uses[_item].Swing?.Arc : null) ?? 150f;
        float deg = blade.Evaluate(t);
        var d = BladeDirection(deg, arc / 150f);
        // The model's +Y (grip to tip) along the blade, its flat across the swing plane, twisted toward the eye.
        var normal = new Vector3(0, 0, -1).Cross(BladeDirection(90f)).Normalized();
        var x = normal;
        var z = x.Cross(d).Normalized();
        var basis = new Basis(x, d, z).Orthonormalized() * new Basis(Vector3.Up, FlatTwist);
        // The hand follows the blade a little, drifting across the view as it comes down.
        float progress = Mathf.Clamp((blade.Max - deg) / Mathf.Max(1f, blade.Max - blade.Min), 0f, 1f);
        var offset = d * HandReach + new Vector3(-HandAcross * progress, 0.04f, 0f);
        return new Pose(offset, basis.GetEuler());
    }

    private Pose ThrustPose(string action, float t)
    {
        var a = _game.ItemUse.Timing.Clip(action); // the rapier's jab or the spear's lunge
        if (a != _thrustClip || _extension == null) _extension = SwingCurve.Extension(_thrustClip = a);
        float ext = _extension.Evaluate(t);
        // Pulled back to the couch through the wind-up, out to the lunge as far as the stab has extended.
        if (t < (a.StrikeStart ?? 0f)) return Pose.Lerp(Level, Couched, Ease(t / Mathf.Max(1e-3f, a.StrikeStart ?? 0f)));
        return Pose.Lerp(t < (a.StrikeEnd ?? 1f) ? Couched : Level, Lunge, ext);
    }
}
