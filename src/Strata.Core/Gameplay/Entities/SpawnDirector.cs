using System.Numerics;
using System.Text.Json;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Determinism;
using Strata.Core.Meshing;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Spawn tuning (data/config/spawning.json).</summary>
public sealed class SpawnConfig
{
    public int CheckInterval { get; init; } = 30;
    public float Chance { get; init; } = 0.18f;
    public int MaxNearbyDay { get; init; } = 4;
    public int MaxNearbyNight { get; init; } = 7;
    public float CountRadius { get; init; } = 60f;
    public float MinDistance { get; init; } = 22f;
    public float MaxDistance { get; init; } = 46f;
    public int Attempts { get; init; } = 12;

    /// <summary>Towns: with this many housed residents within <see cref="TownRadius"/> of a player, spawning slows.</summary>
    public int TownResidents { get; init; } = 2;

    public float TownRadius { get; init; } = 40f;

    /// <summary>Spawn chance multiplier inside a town (world events ignore it).</summary>
    public float TownFactor { get; init; } = 0.25f;

    public static SpawnConfig Load(IDataSource data)
    {
        const string path = "data/config/spawning.json";
        return JsonSerializer.Deserialize<SpawnConfig>(data.ReadText(path), ContentJson.Options) ?? throw new JsonException($"{path} is empty");
    }
}

/// <summary>
/// Natural spawning: around each player, out of view distance, picks a standable cell and a spawn rule whose
/// data-driven conditions (layer, biome, time, light, flags, events) hold there, weighted by rule weight.
/// </summary>
public sealed class SpawnDirector
{
    private readonly GameWorld _world;
    private readonly NpcSystem _npcs;
    private readonly List<(SpawnRuleDef Rule, EnemyDef Enemy, ICondition Condition)> _rules = new();
    private Pcg32 _rng;

    public SpawnDirector(GameWorld world, NpcSystem npcs, SpawnConfig? config = null)
    {
        _world = world;
        _npcs = npcs;
        Config = config ?? new SpawnConfig();
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x5350574EUL));
        var content = world.Session.Content;
        foreach (var rule in content.SpawnRules.All)
            _rules.Add((rule, content.Enemies.Get(rule.Enemy), ConditionCompiler.CompileAll(rule.Conditions)));
    }

    public SpawnConfig Config { get; set; }

    /// <summary>Natural spawning on/off (commands, tests, peaceful building).</summary>
    public bool Enabled { get; set; } = true;

    public void Tick()
    {
        if (!Enabled || _world.Session.Clock.Tick % Config.CheckInterval != 0) return;
        int max = (_world.Session.Clock.IsDay ? Config.MaxNearbyDay : Config.MaxNearbyNight) + _world.Events.MaxNearbyBonus;
        float chance = MathF.Min(1f, Config.Chance * _world.Events.SpawnRateMultiplier);
        foreach (var p in _world.Players)
        {
            if (!p.Alive || _npcs.CountNear(p.Center, Config.CountRadius) >= max) continue;
            float local = InTown(p) && _world.Session.Events.Count == 0 ? chance * Config.TownFactor : chance;
            if (!_rng.Chance(local)) continue;
            TrySpawnNear(p);
        }
    }

    /// <summary>Is the player among enough housed residents to count as being in town?</summary>
    public bool InTown(PlayerEntity p) =>
        _world.Town.Residents.Count(r => r.Alive && r.Home != null && Vector3.Distance(r.Position, p.Position) <= Config.TownRadius) >= Config.TownResidents;

    /// <summary>Attempts one natural spawn near a player; returns the spawned creatures (possibly none).</summary>
    public List<Npc> TrySpawnNear(PlayerEntity player)
    {
        var spawned = new List<Npc>();
        var session = _world.Session;
        var origin = player.FeetCell;
        for (int attempt = 0; attempt < Config.Attempts; attempt++)
        {
            float angle = _rng.NextFloat() * MathF.Tau;
            float dist = _rng.Range(Config.MinDistance, Config.MaxDistance) / Units.CellSize;
            int x = origin.X + (int)(MathF.Sin(angle) * dist), z = origin.Z + (int)(MathF.Cos(angle) * dist);
            var ground = FindGround(x, origin.Y, z);
            var water = FindWater(x, origin.Y, z);
            var cell = ground ?? water;
            if (cell == null) continue;
            var ctx = session.ContextAt(cell.Value);
            ctx.Light = EffectiveLight(cell.Value);
            ctx.Random = _rng;
            var eligible = _rules.Where(r => (r.Rule.Placement == SpawnPlacement.Liquid ? water : ground) != null && r.Condition.Evaluate(ctx)).ToList();
            _rng = ctx.Random;
            if (eligible.Count == 0) continue;
            var pick = Weighted(eligible);
            var at = pick.Rule.Placement == SpawnPlacement.Liquid ? water!.Value : ground!.Value;
            int count = _rng.Range(pick.Rule.GroupMin, pick.Rule.GroupMax);
            for (int i = 0; i < count; i++)
            {
                var feet = new Vector3(at.X + 0.5f + _rng.NextSigned(), at.Y, at.Z + 0.5f + _rng.NextSigned()) * Units.CellSize;
                feet.Y += pick.Rule.Lift ?? (!pick.Enemy.Gravity && pick.Rule.Placement == SpawnPlacement.Ground ? 2.5f : 0f);
                if (_npcs.Spawn(pick.Enemy, feet) is { } npc) spawned.Add(npc);
            }
            return spawned;
        }
        return spawned;
    }

    private (SpawnRuleDef Rule, EnemyDef Enemy, ICondition Condition) Weighted(List<(SpawnRuleDef Rule, EnemyDef Enemy, ICondition Condition)> eligible)
    {
        float total = eligible.Sum(r => r.Rule.Weight);
        float roll = _rng.NextFloat() * total;
        foreach (var r in eligible)
        {
            roll -= r.Rule.Weight;
            if (roll <= 0) return r;
        }
        return eligible[^1];
    }

    /// <summary>First standable cell scanning down a column near <paramref name="y"/> (loaded chunks only).</summary>
    private Int3? FindGround(int x, int y, int z)
    {
        var session = _world.Session;
        for (int dy = 20; dy >= -28; dy--)
            if (session.IsStandable(x, y + dy, z, 4)) return new Int3(x, y + dy, z);
        return null;
    }

    /// <summary>A liquid cell with liquid below it too (a body at least two cells deep), scanning down a column.</summary>
    private Int3? FindWater(int x, int y, int z)
    {
        var world = _world.Session.World;
        for (int dy = 20; dy >= -28; dy--)
        {
            int cy = y + dy;
            if (world.GetLiquid(x, cy, z) != 0 && world.GetLiquid(x, cy - 1, z) != 0 && world.GetBlock(x, cy, z) != Voxels.VoxelWorld.Unknown)
                return new Int3(x, cy - 1, z);
        }
        return null;
    }

    /// <summary>Light a creature would sense: block light, or sky light scaled by daylight.</summary>
    public int EffectiveLight(Int3 cell)
    {
        var chunk = _world.Session.World.GetChunk(ChunkMath.CellToChunk(cell));
        if (chunk?.Light == null) return 15;
        ushort v = chunk.Light[ChunkMath.Index(ChunkMath.CellToLocal(cell))];
        int sky = LightValue.Sky(v);
        return Math.Max(LightValue.BlockMax(v), _world.Session.Clock.IsDay ? sky : sky / 3);
    }
}
