using System.Numerics;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// On-hit effects that act on the owner or spawn more shots (life steal, released projectiles) and latching
/// projectiles that ride on the creature they bit.
/// </summary>
public sealed partial class ProjectileSystem
{
    /// <summary>Heals the owner by <see cref="LifeStealParams.Fraction"/> of the damage dealt (at least 1).</summary>
    private static void StealLife(Projectile p, int dealt, LifeStealParams s)
    {
        if (p.Owner is not { Alive: true } owner) return;
        owner.State.Vitals.Heal(Math.Max(1, (int)MathF.Round(dealt * s.Fraction)));
    }

    /// <summary>Releases <see cref="SpawnProjectilesParams.Count"/> projectiles around the struck creature.</summary>
    private void SpawnFrom(Projectile p, Npc struck, SpawnProjectilesParams s)
    {
        if (!_world.Session.Content.Projectiles.TryGet(s.Projectile, out var def)) return;
        int damage = Math.Max(1, (int)(p.Damage * s.DamageScale));
        var at = p.Position - (p.Velocity.LengthSquared() > 1e-4f ? Vector3.Normalize(p.Velocity) * 0.3f : Vector3.Zero);
        for (int i = 0; i < s.Count; i++)
        {
            var dir = new Vector3(_rng.NextSigned(), 0.3f + _rng.NextFloat() * 0.7f, _rng.NextSigned());
            if (s.Homing && NearestOther(struck, at, i) is { } goal) dir = goal - at;
            dir = dir.LengthSquared() > 1e-4f ? Vector3.Normalize(dir) : Vector3.UnitY;
            Spawn(def, at, dir * s.Speed, p.Owner, damage, p.Knockback * 0.5f, p.DamageClass, p.Crit, p.Source);
        }
    }

    /// <summary>The nearest live creature to <paramref name="at"/> other than the struck one (odd ranks: the second nearest), or null.</summary>
    private Vector3? NearestOther(Npc struck, Vector3 at, int rank)
    {
        Npc? best = null, second = null;
        float bd = 30f * 30f, sd = bd;
        foreach (var npc in _npcs.All)
        {
            if (!npc.Alive || npc.Invulnerable || npc == struck) continue;
            float d = Vector3.DistanceSquared(npc.Center, at);
            if (d < bd) (second, sd, best, bd) = (best, bd, npc, d);
            else if (d < sd) (second, sd) = (npc, d);
        }
        return (rank % 2 == 1 ? second ?? best : best)?.Center;
    }

    /// <summary>Sticks a latching projectile to the creature it bit.</summary>
    private void Latch(Projectile p, Npc npc)
    {
        p.LatchedTo = npc;
        p.LatchOffset = p.Position - npc.Center;
        p.LatchedAt = p.Age;
        p.Velocity = Vector3.Zero;
    }

    /// <summary>A latched projectile rides on its victim and bites again every LatchInterval ticks; false to remove it.</summary>
    private bool TickLatched(Projectile p)
    {
        var npc = p.LatchedTo!;
        if (p.Age > p.Def.Lifetime || !npc.Alive) return false;
        p.Position = npc.Center + p.LatchOffset;
        if ((p.Age - p.LatchedAt) % Math.Max(1, p.Def.LatchInterval) == 0) Strike(p, npc, p.Position);
        return true;
    }
}
