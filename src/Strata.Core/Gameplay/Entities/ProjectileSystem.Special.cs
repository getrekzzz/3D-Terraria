using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Projectiles with a mind of their own: summoned minions, returning boomerangs, homing bolts and fishing bobbers.
/// </summary>
public sealed partial class ProjectileSystem
{
    /// <summary>Advances a minion, bobber or returning boomerang; false when it should be removed.</summary>
    private bool TickSpecial(Projectile p, float dt)
    {
        p.Age++;
        var owner = p.Owner;
        if (p.Def.Minion != null) return owner is { Alive: true } && TickMinion(p, owner, dt);
        if (p.Def.Bobber) return owner is { Alive: true } && TickBobber(p, owner, dt);
        if (p.LatchedTo != null) return TickLatched(p);
        // Returning boomerang: fly straight back through anything, still striking what it meets.
        if (owner == null || !owner.Alive) return false;
        var to = owner.Center - p.Position;
        float dist = to.Length();
        if (dist < 0.9f || p.Age > p.Def.Lifetime * 3) return false;
        float speed = MathF.Max(p.Velocity.Length(), 14f);
        p.Velocity = to / dist * speed;
        p.Position += p.Velocity * dt;
        if (p.Def.Contact && p.Age % 6 == 0) p.HitNpcs.Clear(); // it may strike again on the way back
        if (p.Def.Contact) HitCreatures(p);
        return true;
    }

    /// <summary>
    /// Turns the velocity toward the nearest creature within 25 m (a hostile shot: the nearest player), keeping its speed.
    /// </summary>
    private void Steer(Projectile p, float dt)
    {
        Vector3? goal = null;
        float bestDist = 25f * 25f;
        if (p.Def.Friendly)
        {
            foreach (var npc in _npcs.All)
            {
                if (!npc.Alive || npc.Invulnerable || p.HitNpcs.Contains(npc.Id)) continue;
                float d = Vector3.DistanceSquared(npc.Center, p.Position);
                if (d < bestDist) (goal, bestDist) = (npc.Center, d);
            }
        }
        else
        {
            foreach (var player in _world.Players)
            {
                if (!player.Alive) continue;
                float d = Vector3.DistanceSquared(player.Center, p.Position);
                if (d < bestDist) (goal, bestDist) = (player.Center, d);
            }
        }
        if (goal == null) return;
        float speed = p.Velocity.Length();
        if (speed < 1e-3f) return;
        var want = Vector3.Normalize(goal.Value - p.Position);
        var dir = Vector3.Normalize(Vector3.Lerp(p.Velocity / speed, want, MathF.Min(1f, p.Def.Homing * dt)));
        p.Velocity = dir * speed;
    }

    /// <summary>
    /// Unsummons the owner's oldest minions until a new one fits; sentries (one at a time) are counted apart, so a
    /// sentry never sends a minion home and the other way round.
    /// </summary>
    private void MakeRoomForMinion(PlayerEntity owner, MinionDef minion)
    {
        bool sentry = minion.IsSentry;
        int slots = sentry ? 1 : minion.Slots;
        int allowed = sentry ? 1 : 1 + (int)owner.State.Effects.Stats.Bonus(Stats.StatIds.MaxMinions);
        bool Same(Projectile x) => x.Owner == owner && x.Def.Minion is { } m && m.IsSentry == sentry;
        int used = _items.Where(Same).Sum(x => sentry ? 1 : x.Def.Minion!.Slots);
        for (int i = 0; i < _items.Count && used + slots > allowed; i++)
        {
            var x = _items[i];
            if (!Same(x)) continue;
            used -= sentry ? 1 : x.Def.Minion!.Slots;
            End(i--);
        }
    }

    /// <summary>The owner's minions (summon order).</summary>
    public IEnumerable<Projectile> MinionsOf(PlayerEntity owner) => _items.Where(x => x.Owner == owner && x.Def.Minion != null);

    private bool TickMinion(Projectile p, PlayerEntity owner, float dt)
    {
        var m = p.Def.Minion!;
        int index = 0;
        foreach (var x in _items)
        {
            if (x == p) break;
            if (x.Owner == owner && x.Def.Minion != null) index++;
        }
        // Keep up: a minion left far behind catches up at once (a sentry stays where it was set down).
        bool sentry = m.Speed <= 0;
        if (!sentry && Vector3.DistanceSquared(p.Position, owner.Center) > 45f * 45f) p.Position = owner.Center + new Vector3(0, 2, 0);
        var watch = sentry ? p.Position : owner.Center;
        Npc? target = null;
        float best = m.Range * m.Range;
        foreach (var npc in _npcs.All)
        {
            if (!npc.Alive || npc.Invulnerable) continue;
            float d = Vector3.DistanceSquared(npc.Center, watch);
            if (d < best) (target, best) = (npc, d);
        }
        Vector3 goal;
        if (target == null)
        {
            // Idle: hover in a loose ring above the owner's shoulder.
            float a = index * 2.4f + p.Age * 0.02f;
            goal = owner.Center + new Vector3(MathF.Cos(a) * 1.3f, 1.4f + 0.2f * MathF.Sin(p.Age * 0.08f), MathF.Sin(a) * 1.3f);
        }
        else if (m.Shoot != null)
        {
            // Ranged minions keep a few metres off and fire.
            var away = p.Position - target.Center;
            away.Y = 0;
            away = away.LengthSquared() > 1e-3f ? Vector3.Normalize(away) : Vector3.UnitX;
            goal = target.Center + away * 5f + new Vector3(0, 2.5f, 0);
            if (p.Age >= p.NextShot && _world.Session.Content.Projectiles.TryGet(m.Shoot, out var shot))
            {
                p.NextShot = p.Age + m.ShootInterval;
                var dir = Vector3.Normalize(target.Center - p.Position);
                Spawn(shot, p.Position + dir * 0.3f, dir * m.ShootSpeed, owner, p.Damage, p.Knockback, p.DamageClass, 0);
            }
        }
        else goal = target.Center;
        var desired = goal - p.Position;
        float len = desired.Length();
        var want = len > 0.05f ? desired / len * MathF.Min(m.Speed, len * 4f) : Vector3.Zero;
        p.Velocity = Vector3.Lerp(p.Velocity, want, MathF.Min(1f, 6f * dt));
        p.Position += p.Velocity * dt;
        if (target != null && m.Shoot == null)
        {
            if (p.Age % 20 == 0) p.HitNpcs.Clear(); // ram the same creature again every third of a second
            HitCreatures(p);
        }
        return true;
    }
}
