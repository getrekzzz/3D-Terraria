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
    public int CheckInterval { get; init; } = 60;

    /// <summary>Chance per check that a spawn is attempted by day.</summary>
    public float Chance { get; init; } = 0.10f;

    /// <summary>Chance per check at night (defaults to the day chance).</summary>
    public float? NightChance { get; init; }

    /// <summary>Hostile creatures allowed near a player by day and at night (critters count separately).</summary>
    public int MaxNearbyDay { get; init; } = 4;

    public int MaxNearbyNight { get; init; } = 5;

    /// <summary>Critters (fireflies, bunnies…) allowed near a player.</summary>
    public int MaxCritters { get; init; } = 3;

    public float CountRadius { get; init; } = 60f;
    public float MinDistance { get; init; } = 22f;
    public float MaxDistance { get; init; } = 46f;
    public int Attempts { get; init; } = 12;

    /// <summary>Spawn distances while the player stands in an exclusive-spawn zone (the crypt's halls are close).</summary>
    public float ZoneMinDistance { get; init; } = 9f;

    public float ZoneMaxDistance { get; init; } = 24f;

    /// <summary>No hostile creature appears within this many metres of the world spawn or a player's bed.</summary>
    public float SafeRadius { get; init; } = 30f;

    /// <summary>Surface cells lit by placed lights to at least this level never spawn hostiles (outside world events).</summary>
    public int SafeLight { get; init; } = 8;

    /// <summary>A town (see <see cref="SpawnDirector.InTown"/>): this many housed residents within <see cref="TownRadius"/>.</summary>
    public int TownResidents { get; init; } = 2;

    public float TownRadius { get; init; } = 40f;

    /// <summary>Spawn chance multiplier by the number of living residents within <see cref="TownRadius"/> (the last entry
    /// covers that many or more); world events ignore it.</summary>
    public float[] TownFactors { get; init; } = { 1f, 0.5f, 0.25f, 0.1f };

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
    private readonly Dictionary<SpawnRuleDef, HashSet<string>> _namedBiomes = new();
    private readonly HashSet<string> _exclusive;
    private Pcg32 _rng;

    public SpawnDirector(GameWorld world, NpcSystem npcs, SpawnConfig? config = null)
    {
        _world = world;
        _npcs = npcs;
        Config = config ?? new SpawnConfig();
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x5350574EUL));
        var content = world.Session.Content;
        foreach (var rule in content.SpawnRules.All)
        {
            _rules.Add((rule, content.Enemies.Get(rule.Enemy), ConditionCompiler.CompileAll(rule.Conditions)));
            _namedBiomes[rule] = NamedBiomes(rule);
        }
        _exclusive = content.Biomes.All.Where(b => b.ExclusiveSpawns).Select(b => b.Id).ToHashSet();
    }

    public SpawnConfig Config { get; set; }

    /// <summary>Natural spawning on/off (commands, tests, peaceful building).</summary>
    public bool Enabled { get; set; } = true;

    public void Tick()
    {
        if (!Enabled || _world.Session.Clock.Tick % Config.CheckInterval != 0) return;
        bool day = _world.Session.Clock.IsDay;
        int max = (day ? Config.MaxNearbyDay : Config.MaxNearbyNight) + _world.Events.MaxNearbyBonus;
        float chance = MathF.Min(1f, (day ? Config.Chance : Config.NightChance ?? Config.Chance) * _world.Events.SpawnRateMultiplier);
        foreach (var p in _world.Players)
        {
            if (!p.Alive) continue;
            bool hostiles = _npcs.CountNear(p.Center, Config.CountRadius, critters: false) < max;
            bool critters = _npcs.CountNear(p.Center, Config.CountRadius, critters: true) < Config.MaxCritters;
            if (!hostiles && !critters) continue;
            float local = _world.Session.Events.Count == 0 ? chance * TownFactor(p) : chance;
            if (!_rng.Chance(local)) continue;
            TrySpawnNear(p, hostiles, critters);
        }
    }

    /// <summary>Spawn chance multiplier for the residents living around a player (homeless ones count too).</summary>
    public float TownFactor(PlayerEntity p)
    {
        int n = _world.Town.Residents.Count(r => r.Alive && Vector3.Distance(r.Position, p.Position) <= Config.TownRadius);
        var f = Config.TownFactors;
        return f.Length == 0 ? 1f : f[Math.Min(n, f.Length - 1)];
    }

    /// <summary>
    /// Safe ground: close to the world spawn or a player's bed, or (on the surface, outside world events) lit by
    /// placed lights. Hostile creatures never appear there; they can still walk in.
    /// </summary>
    public bool IsSafe(Int3 cell, World.WorldLayer layer)
    {
        float r = Config.SafeRadius / Units.CellSize;
        foreach (var point in SafePoints())
            if ((cell - point).LengthSquared < r * r) return true;
        return layer == World.WorldLayer.Surface && _world.Session.Events.Count == 0 && BlockLight(cell) >= Config.SafeLight;
    }

    private IEnumerable<Int3> SafePoints()
    {
        yield return _world.Session.SpawnCell;
        foreach (var p in _world.Players)
            if (p.State.SpawnPoints.TryGetValue(_world.Session.Settings.Name, out var bed) && bed.Length == 3)
                yield return new Int3(bed[0], bed[1], bed[2]);
    }

    private static Conditions.ConditionContext WithRandom(Conditions.ConditionContext target, Conditions.ConditionContext source)
    {
        target.Random = source.Random;
        return target;
    }

    /// <summary>Is the player among enough housed residents to count as being in town?</summary>
    public bool InTown(PlayerEntity p) =>
        _world.Town.Residents.Count(r => r.Alive && r.Home != null && Vector3.Distance(r.Position, p.Position) <= Config.TownRadius) >= Config.TownResidents;

    /// <summary>Attempts one natural spawn near a player; returns the spawned creatures (possibly none).</summary>
    public List<Npc> TrySpawnNear(PlayerEntity player, bool hostiles = true, bool critters = true)
    {
        var spawned = new List<Npc>();
        var session = _world.Session;
        var origin = player.FeetCell;
        bool inZone = session.ZonesAt(origin).Any(_exclusive.Contains);
        float min = inZone ? Config.ZoneMinDistance : Config.MinDistance, max = inZone ? Config.ZoneMaxDistance : Config.MaxDistance;
        for (int attempt = 0; attempt < Config.Attempts; attempt++)
        {
            float angle = _rng.NextFloat() * MathF.Tau;
            float dist = _rng.Range(min, max) / Units.CellSize;
            int x = origin.X + (int)(MathF.Sin(angle) * dist), z = origin.Z + (int)(MathF.Cos(angle) * dist);
            var ground = FindGround(x, origin.Y, z);
            var water = FindWater(x, origin.Y, z);
            var cell = ground ?? water;
            if (cell == null) continue;
            var ctx = session.ContextAt(cell.Value);
            ctx.Light = EffectiveLight(cell.Value);
            ctx.Random = _rng;
            ctx.PlayerMaxLife = player.State.Vitals.MaxLife;
            var atPlayer = session.ContextAt(origin);
            atPlayer.Light = EffectiveLight(origin);
            atPlayer.PlayerMaxLife = player.State.Vitals.MaxLife;
            var exclusive = ctx.Biomes.Where(_exclusive.Contains).ToList();
            bool hostileHere = hostiles && !IsSafe(cell.Value, ctx.Layer);
            var eligible = _rules.Where(r => (r.Enemy.HasTag("critter") ? critters : hostileHere)
                && (r.Rule.Placement == SpawnPlacement.Liquid ? water : ground) != null
                && (exclusive.Count == 0 || exclusive.Any(_namedBiomes[r.Rule].Contains))
                && (r.Rule.MaxAlive <= 0 || _npcs.All.Count(n => n.Def == r.Enemy) < r.Rule.MaxAlive)
                && r.Condition.Evaluate(r.Rule.AtPlayer ? WithRandom(atPlayer, ctx) : ctx)).ToList();
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

    /// <summary>
    /// Enemies likely to appear near a player soon (for preloading their models): those whose spawn rules pass where
    /// the player stands at the current time, and those of rules naming a biome the player is in.
    /// </summary>
    public List<EnemyDef> LikelyEnemies(PlayerEntity p)
    {
        var ctx = _world.Session.ContextAt(p.FeetCell);
        ctx.PlayerMaxLife = p.State.Vitals.MaxLife;
        return LikelyEnemies(ctx);
    }

    /// <summary>
    /// <see cref="LikelyEnemies(PlayerEntity)"/> for a given context. Light is ignored (taken as dark, where most rules
    /// pass) and chance rolls use a private generator, so asking never changes what actually spawns.
    /// </summary>
    public List<EnemyDef> LikelyEnemies(ConditionContext ctx)
    {
        ctx.Light = 0;
        ctx.Random = new Pcg32(0x4C494B45UL);
        var likely = new List<EnemyDef>();
        foreach (var (rule, enemy, condition) in _rules)
        {
            if (likely.Contains(enemy)) continue;
            bool named = false;
            foreach (var biome in ctx.Biomes)
                if (_namedBiomes[rule].Contains(biome)) { named = true; break; }
            if (named || condition.Evaluate(ctx)) likely.Add(enemy);
        }
        return likely;
    }

    /// <summary>Biomes a rule asks for by name (top-level <c>biome any</c> conditions), for exclusive zones.</summary>
    private static HashSet<string> NamedBiomes(SpawnRuleDef rule)
    {
        var names = new HashSet<string>();
        foreach (var c in rule.Conditions.Where(c => c.Type == "biome"))
            if (c.Raw.TryGetProperty("any", out var any) && any.ValueKind == JsonValueKind.Array)
                foreach (var v in any.EnumerateArray())
                    if (v.GetString() is { } id) names.Add(id);
        return names;
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

    /// <summary>Light from placed lights and glowing blocks only (no sky light).</summary>
    public int BlockLight(Int3 cell)
    {
        var chunk = _world.Session.World.GetChunk(ChunkMath.CellToChunk(cell));
        return chunk?.Light == null ? 0 : LightValue.BlockMax(chunk.Light[ChunkMath.Index(ChunkMath.CellToLocal(cell))]);
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
