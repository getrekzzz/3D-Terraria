using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Entities;

/// <summary>A damage event for presentation (damage numbers, sounds, flashes).</summary>
public readonly record struct DamageEvent(Vector3 Position, int Amount, bool Crit, bool ToPlayer);

/// <summary>
/// Owns every live creature: brains, movement against voxels, contact damage to players, taking hits
/// (knockback, immunity), death with loot and coins, and despawning far from players.
/// </summary>
public sealed class NpcSystem
{
    public const int MaxNpcs = 200;
    public const float DespawnDistance = 90f;
    public const int DespawnDelayTicks = 180;
    public const int PlayerHitImmunity = 40;

    private readonly GameWorld _world;
    private readonly List<Npc> _npcs = new();
    private readonly Dictionary<EnemyDef, IBrain> _brains = new();
    private readonly LootRoller _loot;
    private readonly BrainContext _ctx;
    private long _nextId = 1;
    private Pcg32 _rng;

    public NpcSystem(GameWorld world)
    {
        _world = world;
        _loot = new LootRoller(world.Session.Content);
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x4E5043UL));
        _ctx = new BrainContext { World = world.Session.World };
    }

    public IReadOnlyList<Npc> All => _npcs;

    public event Action<Npc>? Spawned;
    public event Action<Npc>? Removed;
    public event Action<Npc>? Killed;
    public event Action<DamageEvent>? Damaged;

    /// <summary>A player was hit: (player, damage, knockback velocity in m/s).</summary>
    public event Action<PlayerEntity, int, Vector3>? PlayerHurt;

    public Npc? Spawn(EnemyDef def, Vector3 feet)
    {
        if (_npcs.Count >= MaxNpcs) return null;
        var npc = new Npc(_nextId++, def, feet) { Yaw = _rng.NextFloat() * MathF.Tau };
        _npcs.Add(npc);
        Spawned?.Invoke(npc);
        return npc;
    }

    public IBrain BrainOf(EnemyDef def)
    {
        if (!_brains.TryGetValue(def, out var brain)) _brains[def] = brain = BrainFactory.Create(def.Brain);
        return brain;
    }

    public void Tick()
    {
        const float dt = 1f / Units.TicksPerSecond;
        var session = _world.Session;
        _ctx.IsDay = session.Clock.IsDay;
        for (int i = _npcs.Count - 1; i >= 0; i--)
        {
            var npc = _npcs[i];
            npc.Age++;
            if (npc.ImmuneTicks > 0) npc.ImmuneTicks--;
            if (npc.HurtFlash > 0) npc.HurtFlash--;
            if (npc.Buffs.Tick()) npc.RecomputeEffects();
            if (npc.Effects.DamagePerSecond > 0 && npc.Age % Units.TicksPerSecond == 0)
            {
                npc.Life -= npc.Effects.DamagePerSecond;
                Damaged?.Invoke(new DamageEvent(npc.Center + new Vector3(0, npc.HalfExtents.Y + 0.3f, 0), npc.Effects.DamagePerSecond, false, false));
                if (npc.Life <= 0)
                {
                    Kill(npc);
                    continue;
                }
            }
            if (session.World.GetBlock(VoxelMotion.CellOf(npc.Center)) == VoxelWorld.Unknown)
            {
                if (++npc.FarTicks > DespawnDelayTicks) Remove(i);
                continue; // chunk not loaded: frozen
            }
            var target = Nearest(npc.Center, out float distance);
            if (distance > DespawnDistance)
            {
                if (++npc.FarTicks > DespawnDelayTicks)
                {
                    Remove(i);
                    continue;
                }
            }
            else
            {
                npc.FarTicks = 0;
            }
            _ctx.Target = target;
            _ctx.TargetDistance = distance;
            _ctx.OnSurface = session.Generator.Plan.Bands.LayerAt(npc.Cell.Y) == World.WorldLayer.Surface;
            _ctx.Rng = _rng;
            var brain = BrainOf(npc.Def);
            brain.Think(npc, _ctx);
            _rng = _ctx.Rng;
            if (brain.UsesGravity && npc.Def.Gravity) npc.Velocity.Y = MathF.Max(npc.Velocity.Y - BrainContext.Gravity * dt, -30f);
            Move(npc, brain, dt);
            if (npc.Def.ContactDamage && target != null) ContactDamage(npc, target);
            if (npc.Position.Y < 0) Remove(i);
        }
    }

    private void Move(Npc npc, IBrain brain, float dt)
    {
        var intended = npc.Velocity;
        var center = npc.Center;
        if (npc.Def.CollideTerrain)
        {
            npc.OnGround = VoxelMotion.Move(_world.Session.World, ref center, ref npc.Velocity, npc.HalfExtents, dt);
        }
        else
        {
            center += npc.Velocity * dt;
            npc.OnGround = false;
        }
        npc.Position = center - new Vector3(0, npc.HalfExtents.Y, 0);
        brain.AfterMove(npc, intended);
    }

    private void ContactDamage(Npc npc, PlayerEntity player)
    {
        if (!player.Alive || player.State.Vitals.ImmunityTicks > 0 || npc.Def.Damage <= 0) return;
        var pBox = (player.Center - PlayerEntity.HalfExtents, player.Center + PlayerEntity.HalfExtents);
        if (!Npc.Overlaps(npc.Box, pBox)) return;
        var state = player.State;
        var hit = DamageModel.Roll(npc.Def.Damage, DamageClass.None, 0, 6f, null, state.Defense,
            DamageModel.DefenseFactor(_world.Session.Settings.Difficulty), state.Effects.Stats.Bonus(Stats.StatIds.EnduranceReduction),
            state.Effects.Stats.Bonus(Stats.StatIds.KnockbackResist), ref _rng);
        if (!state.Vitals.Hurt(hit.Damage, PlayerHitImmunity)) return;
        var away = BrainFactory.Flat(player.Position - npc.Position);
        if (away == Vector3.Zero) away = new Vector3(MathF.Sin(npc.Yaw), 0, MathF.Cos(npc.Yaw));
        var knock = away * (2.5f + hit.Knockback * 0.6f) + new Vector3(0, 4.5f, 0);
        Damaged?.Invoke(new DamageEvent(player.Center + new Vector3(0, 1f, 0), hit.Damage, false, true));
        PlayerHurt?.Invoke(player, hit.Damage, knock);
    }

    /// <summary>Applies a hit from a player. Returns false if the NPC was immune (melee) or already dead.</summary>
    public bool Hit(Npc npc, PlayerEntity? attacker, int baseDamage, DamageClass damageClass, int critChance, float knockback, Vector3 from, bool melee)
    {
        if (!npc.Alive || (melee && npc.ImmuneTicks > 0)) return false;
        var hit = DamageModel.Roll(baseDamage, damageClass, critChance, knockback, attacker?.State.Effects.Stats, npc.Def.Defense,
            0.5f, 0f, npc.Def.KnockbackResist, ref _rng);
        npc.Life -= hit.Damage;
        npc.HurtFlash = 8;
        if (melee) npc.ImmuneTicks = 10;
        var away = BrainFactory.Flat(npc.Position - from);
        npc.Velocity += away * hit.Knockback * 1.2f + new Vector3(0, hit.Knockback * 0.5f, 0);
        Damaged?.Invoke(new DamageEvent(npc.Center + new Vector3(0, npc.HalfExtents.Y + 0.3f, 0), hit.Damage, hit.Crit, false));
        if (npc.Life <= 0) Kill(npc);
        return true;
    }

    public void Kill(Npc npc)
    {
        npc.Life = 0;
        var cell = npc.Cell;
        var drops = new List<ItemStack>();
        if (npc.Def.Loot != null)
        {
            var rng = new Pcg32(Hash.Combine((ulong)npc.Id, _world.Session.Clock.Tick));
            drops.AddRange(_loot.Roll(npc.Def.Loot, ref rng, _world.Session.ContextAt(cell)));
        }
        if (npc.Def.Value > 0) drops.AddRange(Coins.ToStacks(_world.Session.Content, (long)(npc.Def.Value * _rng.Range(0.75f, 1.25f))));
        foreach (var stack in drops)
            _world.Drops.Spawn(stack, npc.Center, new Vector3(_rng.NextSigned() * 1.5f, 3f, _rng.NextSigned() * 1.5f));
        Killed?.Invoke(npc);
        int index = _npcs.IndexOf(npc);
        if (index >= 0) Remove(index);
    }

    /// <summary>Applies a buff (e.g. from a projectile) and refreshes derived effects.</summary>
    public void AddBuff(Npc npc, BuffDef buff, int ticks)
    {
        npc.Buffs.Add(buff, ticks);
        npc.RecomputeEffects();
    }

    public int CountNear(Vector3 point, float radius) => _npcs.Count(n => Vector3.DistanceSquared(n.Center, point) < radius * radius);

    public void Clear()
    {
        for (int i = _npcs.Count - 1; i >= 0; i--) Remove(i);
    }

    private PlayerEntity? Nearest(Vector3 point, out float distance)
    {
        PlayerEntity? best = null;
        distance = float.MaxValue;
        foreach (var p in _world.Players)
        {
            if (!p.Alive) continue;
            float d = Vector3.Distance(p.Center, point);
            if (d < distance)
            {
                distance = d;
                best = p;
            }
        }
        return best;
    }

    private void Remove(int index)
    {
        var npc = _npcs[index];
        _npcs.RemoveAt(index);
        Removed?.Invoke(npc);
    }
}
