using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Entities;

public sealed class Projectile
{
    public required long Id { get; init; }
    public required ProjectileDef Def { get; init; }
    public Vector3 Position;
    public Vector3 Velocity;
    public required PlayerEntity? Owner { get; init; }
    public required int Damage { get; init; }
    public required float Knockback { get; init; }
    public required DamageClass DamageClass { get; init; }
    public required int Crit { get; init; }
    public int Age { get; set; }
    public int PierceLeft { get; set; }
    public int BouncesLeft { get; set; }
    public HashSet<long> HitNpcs { get; } = new();
}

/// <summary>
/// Simulates projectiles: gravity and drag, terrain collision (bounce or stop), creature hits with pierce,
/// on-hit effects (buffs, explosions) and lifetime. Spawned from item use shot requests.
/// </summary>
public sealed class ProjectileSystem
{
    private readonly GameWorld _world;
    private readonly NpcSystem _npcs;
    private readonly List<Projectile> _items = new();
    private long _nextId = 1;
    private Pcg32 _rng;

    public ProjectileSystem(GameWorld world, NpcSystem npcs, ItemUseSystem uses)
    {
        _world = world;
        _npcs = npcs;
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x50524FUL));
        uses.Shot += Fire;
    }

    public IReadOnlyList<Projectile> All => _items;

    public event Action<Projectile>? Spawned;

    /// <summary>A projectile ended (hit, expired or stopped) at its current position.</summary>
    public event Action<Projectile>? Removed;

    private void Fire(ShotRequest shot)
    {
        var p = shot.Player;
        var spread = shot.Weapon.Shoot!.Spread * MathF.PI / 180f;
        var dir = p.Aim;
        if (spread > 0)
        {
            var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY) + new Vector3(1e-4f, 0, 0));
            var up = Vector3.Cross(side, dir);
            dir = Vector3.Normalize(dir + side * _rng.NextSigned() * spread + up * _rng.NextSigned() * spread * 0.5f);
        }
        var item = shot.Weapon.Item;
        Spawn(shot.Projectile, p.Eye + dir * 0.4f, dir * MathF.Max(shot.Speed, 1f), p, shot.Damage, shot.Knockback, item.DamageType, item.Crit + Combat.DamageModel.BaseCritChance);
    }

    public Projectile Spawn(ProjectileDef def, Vector3 position, Vector3 velocity, PlayerEntity? owner, int damage, float knockback, DamageClass damageClass, int crit)
    {
        var proj = new Projectile
        {
            Id = _nextId++, Def = def, Position = position, Velocity = velocity, Owner = owner, Damage = damage, Knockback = knockback,
            DamageClass = damageClass, Crit = crit, PierceLeft = def.Pierce, BouncesLeft = def.Bounces,
        };
        _items.Add(proj);
        Spawned?.Invoke(proj);
        return proj;
    }

    public void Tick()
    {
        const float dt = 1f / Units.TicksPerSecond;
        var world = _world.Session.World;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var p = _items[i];
            if (++p.Age > p.Def.Lifetime || world.GetBlock(VoxelMotion.CellOf(p.Position)) == VoxelWorld.Unknown)
            {
                End(i);
                continue;
            }
            p.Velocity.Y -= p.Def.Gravity * dt;
            if (p.Def.Drag > 0) p.Velocity *= 1f - p.Def.Drag * dt;
            if (p.Def.CollideTerrain && !MoveThroughTerrain(p, dt))
            {
                End(i);
                continue;
            }
            if (!p.Def.CollideTerrain) p.Position += p.Velocity * dt;
            if (p.Def.Friendly && HitCreatures(p))
            {
                End(i);
                continue;
            }
        }
    }

    /// <summary>Moves with voxel collision; returns false when the projectile should end.</summary>
    private bool MoveThroughTerrain(Projectile p, float dt)
    {
        float r = MathF.Min(p.Def.Radius * Units.CellSize, 0.2f);
        var half = new Vector3(r);
        var before = p.Velocity;
        VoxelMotion.Move(_world.Session.World, ref p.Position, ref p.Velocity, half, dt);
        bool collided = p.Velocity.X != before.X || p.Velocity.Y != before.Y || p.Velocity.Z != before.Z;
        if (!collided) return true;
        if (p.BouncesLeft-- <= 0)
        {
            ApplyOnHit(p, null);
            return false;
        }
        if (p.Velocity.X == 0 && before.X != 0) p.Velocity.X = -before.X * 0.7f;
        if (p.Velocity.Y == 0 && before.Y != 0) p.Velocity.Y = -before.Y * 0.7f;
        if (p.Velocity.Z == 0 && before.Z != 0) p.Velocity.Z = -before.Z * 0.7f;
        return true;
    }

    /// <summary>Hits creatures touching the projectile; returns true when its pierce is used up.</summary>
    private bool HitCreatures(Projectile p)
    {
        float r = p.Def.Radius * Units.CellSize;
        foreach (var npc in _npcs.All.ToList())
        {
            if (!npc.Alive || p.HitNpcs.Contains(npc.Id)) continue;
            if (!npc.Volumes().Any(v => Vector3.DistanceSquared(Vector3.Clamp(p.Position, v.Min, v.Max), p.Position) <= r * r)) continue;
            p.HitNpcs.Add(npc.Id);
            _npcs.Hit(npc, p.Owner, p.Damage, p.DamageClass, p.Crit, p.Knockback, p.Position - p.Velocity * 0.05f, melee: false);
            ApplyOnHit(p, npc);
            if (p.PierceLeft >= 0 && --p.PierceLeft <= 0) return true;
        }
        return false;
    }

    private void ApplyOnHit(Projectile p, Npc? npc)
    {
        foreach (var spec in p.Def.OnHit)
        {
            switch (spec.Type)
            {
                case ProjectileHitSpecs.ApplyBuff when npc is { Alive: true }:
                    var b = spec.Parse<ApplyBuffParams>();
                    if (_rng.Chance(b.Chance) && _world.Session.Content.Buffs.TryGet(b.Buff, out var buff)) _npcs.AddBuff(npc, buff, b.Ticks);
                    break;
                case ProjectileHitSpecs.Explode:
                    Explode(p, spec.Parse<ExplodeParams>());
                    break;
            }
        }
    }

    private void Explode(Projectile p, ExplodeParams e)
    {
        float radius = e.Radius * Units.CellSize;
        foreach (var npc in _npcs.All.ToList())
            if (npc.Alive && Vector3.Distance(npc.Center, p.Position) <= radius + npc.HalfExtents.X)
                _npcs.Hit(npc, p.Owner, e.Damage > 0 ? e.Damage : p.Damage, p.DamageClass, 0, p.Knockback + 4, p.Position, melee: false);
        if (!e.DestroyTerrain) return;
        var center = VoxelMotion.CellOf(p.Position);
        int r = (int)MathF.Ceiling(e.Radius);
        var cells = new List<Int3>();
        for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y + z * z <= e.Radius * e.Radius) cells.Add(center + new Int3(x, y, z));
        _world.Editor.Mine(cells, e.PickPower, 1e6f, _world.Session.Clock.Tick);
    }

    private void End(int index)
    {
        var p = _items[index];
        _items.RemoveAt(index);
        Removed?.Invoke(p);
    }
}
