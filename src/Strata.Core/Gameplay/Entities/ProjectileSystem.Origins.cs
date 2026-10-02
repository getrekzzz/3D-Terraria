using System.Numerics;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Where a player's shot starts (P3-09'): the business end of the weapon for its use style (the style's
/// <c>holdout</c> in use_styles.json, else the hand), the sky above the crosshair point for Starfury-like weapons, the
/// crosshair point itself for minions and "cursor" shots, or the eye.
/// </summary>
public sealed partial class ProjectileSystem
{
    /// <summary>Furthest a minion or cursor shot appears from the eye (metres).</summary>
    public const float MaxCursorReach = 12f;

    /// <summary>A minion or cursor shot appears this far short of the surface the crosshair rests on (metres).</summary>
    public const float CursorStandOff = 1f;

    private (Vector3 From, Vector3 Dir) ShotStart(ShotRequest shot)
    {
        var p = shot.Player;
        var world = _world.Session.World;
        string from = shot.Projectile.Minion != null ? "cursor" : shot.Weapon.Shoot!.From;
        switch (from)
        {
            case "sky":
            {
                var target = AimPoint(p, float.MaxValue);
                var scatter = shot.Weapon.Shoot!.SkyScatter;
                var start = target + new Vector3(_rng.NextSigned() * scatter, shot.Weapon.Shoot.SkyHeight, _rng.NextSigned() * scatter);
                return (start, Vector3.Normalize(target - start));
            }
            case "cursor":
            {
                float reach = MathF.Min(MathF.Max(p.AimDistance - CursorStandOff, 1f), MaxCursorReach);
                return (AimPoint(p, reach), p.Aim);
            }
            case "eye":
                return (p.Eye, p.Aim);
            default:
            {
                var origin = ShotGeometry.Origin(world, p, Holdout(shot.Weapon) ?? ShotGeometry.HandOffset);
                return (origin, ShotGeometry.Direction(p, origin));
            }
        }
    }

    /// <summary>The crosshair point, at most <paramref name="reach"/> metres from the eye (never nearer than 1 m).</summary>
    private static Vector3 AimPoint(PlayerEntity p, float reach)
    {
        float d = p.AimDistance > 1f ? p.AimDistance : 1f; // NaN clamps too
        return p.Eye + p.Aim * MathF.Min(d, reach);
    }

    /// <summary>The weapon's business end in the facing frame (X right, Y up, Z forward) for its use style, if it has one.</summary>
    private Vector3? Holdout(Items.ItemUseInfo weapon)
    {
        var name = weapon.Style.ToString();
        name = char.ToLowerInvariant(name[0]) + name[1..];
        return _world.Styles.Styles.TryGetValue(name, out var entry) && entry.Holdout is { } h ? new Vector3(h.Right, -h.Down, h.Forward) : null;
    }
}
