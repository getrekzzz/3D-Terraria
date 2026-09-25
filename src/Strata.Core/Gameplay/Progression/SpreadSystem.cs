using System.Text.Json;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Loading;
using Strata.Core.Determinism;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Progression;

/// <summary>Spread tuning (data/config/spread.json).</summary>
public sealed class SpreadConfig
{
    public const string Path = "data/config/spread.json";

    /// <summary>Ticks between spread updates.</summary>
    public int Interval { get; init; } = 30;

    /// <summary>Random cells tried per loaded chunk per update.</summary>
    public int CellsPerChunk { get; init; } = 3;

    public static SpreadConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<SpreadConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}

/// <summary>
/// Spreading biomes: random cells of loaded chunks are visited; a source block of an active spread rule converts one
/// neighbour through the rule's conversion table (radiant turf creeping over grass, blight eating stone).
/// </summary>
public sealed class SpreadSystem
{
    private readonly GameWorld _world;
    private readonly List<Rule> _rules = new();
    private readonly List<Rule>?[] _bySource;
    private Pcg32 _rng;

    public SpreadSystem(GameWorld world, SpreadConfig? config = null)
    {
        _world = world;
        Config = config ?? new SpreadConfig();
        var content = world.Session.Content;
        _bySource = new List<Rule>?[content.Blocks.Count];
        foreach (var def in content.SpreadRules.All)
        {
            var conversion = content.Conversions.Get(def.Conversion);
            var map = new ushort[content.Blocks.Count];
            foreach (var (from, to) in conversion.Map) map[content.Blocks.Get(from).RuntimeId] = (ushort)content.Blocks.Get(to).RuntimeId;
            var rule = new Rule(def.RequiresFlag, def.Chance, Math.Max(1, def.Radius), map);
            _rules.Add(rule);
            var sources = def.Sources.Length > 0 ? def.Sources : conversion.Map.Values.Distinct().ToArray();
            foreach (var s in sources) (_bySource[content.Blocks.Get(s).RuntimeId] ??= new List<Rule>()).Add(rule);
        }
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x535052454144UL));
    }

    public SpreadConfig Config { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Blocks converted so far this session (diagnostics).</summary>
    public long Conversions { get; private set; }

    public void Tick()
    {
        if (!Enabled || _rules.Count == 0 || _world.Session.Clock.Tick % Config.Interval != 0) return;
        Update(Config.CellsPerChunk);
    }

    /// <summary>One spread update over every loaded chunk; returns the number of blocks converted.</summary>
    public int Update(int cellsPerChunk)
    {
        var flags = _world.Session.Flags;
        if (!_rules.Any(r => r.Flag == null || flags.Contains(r.Flag))) return 0;
        var world = _world.Session.World;
        var batch = world.BeginEdit();
        int converted = 0;
        foreach (var chunk in world.Chunks)
        {
            if (chunk.IsUniform && _bySource[chunk.UniformBlock] == null) continue;
            var origin = ChunkMath.ChunkOrigin(chunk.Coord);
            for (int n = 0; n < cellsPerChunk; n++)
            {
                int i = _rng.NextInt(ChunkMath.Volume);
                var rules = _bySource[chunk.GetBlock(i)];
                if (rules == null) continue;
                var rule = rules[_rng.NextInt(rules.Count)];
                if ((rule.Flag != null && !flags.Contains(rule.Flag)) || !_rng.Chance(rule.Chance)) continue;
                var cell = origin + new Int3(i & (ChunkMath.Size - 1), i >> (ChunkMath.Shift * 2), (i >> ChunkMath.Shift) & (ChunkMath.Size - 1));
                var target = cell + new Int3(_rng.Range(-rule.Radius, rule.Radius), _rng.Range(-rule.Radius, rule.Radius), _rng.Range(-rule.Radius, rule.Radius));
                ushort block = batch.GetBlock(target);
                if (block == VoxelWorld.Unknown || rule.Map[block] == 0) continue;
                batch.SetBlock(target, rule.Map[block], batch.GetShape(target.X, target.Y, target.Z));
                converted++;
            }
        }
        if (converted > 0) _world.Streamer.MarkDirty(_world.Session.Commit(batch), urgent: false);
        Conversions += converted;
        return converted;
    }

    private sealed record Rule(string? Flag, float Chance, int Radius, ushort[] Map);
}
