using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Physics;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Tethered projectiles (P3-12): a yoyo stays out while its owner holds the use button (up to its tether lifetime),
/// chasing the crosshair point within its range of the eye and sliding along the terrain instead of ending on it;
/// it strikes what it touches, every creature again each 10 ticks. Released, run out, or the yoyo put away, it comes
/// back to the hand like a boomerang.
/// </summary>
public sealed partial class ProjectileSystem
{
    /// <summary>How hard (m/s²) a yoyo steers toward the crosshair.</summary>
    private const float YoyoPull = 60f;

    /// <summary>Ticks between a yoyo's repeat strikes on the same creature.</summary>
    private const int YoyoRehit = 10;

    /// <summary>The player's tethered projectile out now (their yoyo or flail), or null.</summary>
    public Projectile? TetherOf(PlayerEntity owner)
    {
        foreach (var p in _items)
            if (p.Owner == owner && p.Def.Tether != null) return p;
        return null;
    }

    private bool TickYoyo(Projectile p, PlayerEntity owner, float dt)
    {
        var tether = p.Def.Tether!;
        bool held = owner.UseHeld && owner.State.Inventory.Held.Item == p.Source;
        if (!held || (tether.Lifetime > 0 && p.Age > tether.Lifetime)) return ReelIn(p); // the trip home has its own clock (TickSpecial)
        float range = tether.Range * Units.CellSize;
        var goal = AimPoint(owner, range);
        var to = goal - p.Position;
        float dist = to.Length();
        var want = dist > 1e-3f ? to / dist * MathF.Min(tether.Speed, dist * 8f) : Vector3.Zero;
        var change = want - p.Velocity;
        float step = YoyoPull * dt;
        p.Velocity += change.Length() > step ? Vector3.Normalize(change) * step : change;
        float r = MathF.Min(p.Def.Radius * Units.CellSize, 0.2f);
        VoxelMotion.Move(_world.Session.World, ref p.Position, ref p.Velocity, new Vector3(r), dt);
        // The string never stretches past its range.
        var fromEye = p.Position - owner.Eye;
        if (fromEye.Length() > range) p.Position = owner.Eye + Vector3.Normalize(fromEye) * range;
        if (p.Def.Contact)
        {
            if (p.Age % YoyoRehit == 0) p.HitNpcs.Clear();
            HitCreatures(p);
        }
        return true;
    }
}
