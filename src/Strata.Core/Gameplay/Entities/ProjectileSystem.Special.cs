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
    /// <summary>Table rolled for a catch (conditions see the bobber's biome, depth and the rod's fishing power).</summary>
    public const string FishingTable = Content.Index.ItemSourceIndex.FishingTable;

    private LootRoller? _loot;

    /// <summary>Advances a minion, bobber or returning boomerang; false when it should be removed.</summary>
    private bool TickSpecial(Projectile p, float dt)
    {
        p.Age++;
        var owner = p.Owner;
        if (p.Def.Minion != null) return owner is { Alive: true } && TickMinion(p, owner, dt);
        if (p.Def.Bobber) return owner is { Alive: true } && TickBobber(p, owner, dt);
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

    /// <summary>Unsummons the owner's oldest minions until one using <paramref name="slots"/> fits.</summary>
    private void MakeRoomForMinion(PlayerEntity owner, int slots)
    {
        int allowed = 1 + (int)owner.State.Effects.Stats.Bonus(Stats.StatIds.MaxMinions);
        int used = _items.Where(x => x.Owner == owner && x.Def.Minion != null).Sum(x => x.Def.Minion!.Slots);
        for (int i = 0; i < _items.Count && used + slots > allowed; i++)
        {
            var x = _items[i];
            if (x.Owner != owner || x.Def.Minion == null) continue;
            used -= x.Def.Minion.Slots;
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

    /// <summary>The player's bobber, if one is out.</summary>
    public Projectile? BobberOf(PlayerEntity owner) => _items.FirstOrDefault(x => x.Owner == owner && x.Def.Bobber);

    private bool TickBobber(Projectile p, PlayerEntity owner, float dt)
    {
        var world = _world.Session.World;
        if (Vector3.DistanceSquared(owner.Center, p.Position) > 40f * 40f) return false; // the line snaps
        var cell = VoxelMotion.CellOf(p.Position);
        var liquid = LiquidCode.Type(world.GetLiquid(cell.X, cell.Y, cell.Z));
        bool fishable = liquid is LiquidType.Water or LiquidType.Honey;
        if (!p.Floating)
        {
            p.Velocity.Y -= p.Def.Gravity * dt;
            if (fishable)
            {
                p.Floating = true;
                p.Velocity = Vector3.Zero;
                ScheduleBite(p);
                return true;
            }
            float r = MathF.Min(p.Def.Radius * Units.CellSize, 0.2f);
            VoxelMotion.Move(world, ref p.Position, ref p.Velocity, new Vector3(r), dt);
            p.Velocity.X *= 0.99f;
            p.Velocity.Z *= 0.99f;
            return true;
        }
        if (!fishable)
        {
            // Drifted out of (or drained from under) the water: sink again.
            p.Floating = false;
            p.Biting = false;
            return true;
        }
        // Rise to the surface of the liquid column and rest there.
        var above = cell + new Int3(0, 1, 0);
        bool submerged = LiquidCode.Type(world.GetLiquid(above.X, above.Y, above.Z)) != LiquidType.None;
        float level = (world.GetLiquid(cell.X, cell.Y, cell.Z) & 0x1F) / (float)LiquidCode.MaxLevel;
        float surface = submerged ? p.Position.Y + 0.5f : (cell.Y + level) * Units.CellSize;
        p.Position.Y += MathF.Min(surface - p.Position.Y - 0.02f, 2f * dt);
        if (!p.Biting && p.Age >= p.BiteAt)
        {
            p.Biting = true;
            p.BiteAt = p.Age + 50; // the fish holds on for a moment
        }
        else if (p.Biting && p.Age >= p.BiteAt)
        {
            p.Biting = false;
            ScheduleBite(p);
        }
        return true;
    }

    private void ScheduleBite(Projectile p)
    {
        float power = p.Source is { } rod ? _world.Uses[rod].Fishing?.Power ?? 5 : 5;
        float wait = _rng.Range(90, 420) * 25f / (power + 20f);
        p.BiteAt = p.Age + (int)wait;
    }

    /// <summary>
    /// Reels the bobber in: a biting fish yields a catch from the <see cref="FishingTable"/> (rolled for the bobber's
    /// spot and the rod's fishing power); otherwise nothing.
    /// </summary>
    public List<ItemStack> Reel(Projectile bobber)
    {
        var catches = new List<ItemStack>();
        int index = _items.IndexOf(bobber);
        if (index < 0) return catches;
        if (bobber.Biting && _world.Session.Content.LootTables.Contains(FishingTable))
        {
            var cell = VoxelMotion.CellOf(bobber.Position);
            var ctx = _world.Session.ContextAt(cell);
            ctx.FishingPower = bobber.Source is { } rod ? _world.Uses[rod].Fishing?.Power ?? 5 : 5;
            ctx.Random = _rng;
            _loot ??= new LootRoller(_world.Session.Content);
            catches.AddRange(_loot.Roll(FishingTable, ref _rng, ctx));
        }
        End(index);
        return catches;
    }
}
