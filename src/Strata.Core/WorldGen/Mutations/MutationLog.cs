using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.Diagnostics;
using Strata.Core.Persistence;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.WorldGen.Mutations;

/// <summary>
/// The ordered list of world mutations that have happened (saved with the world). A chunk's mutation level is how many
/// of them it already carries; anything behind catches up with <see cref="Apply"/>. Appends happen on the main thread;
/// workers read an immutable snapshot, so a chunk produced during an append is simply one level behind and is caught
/// up once installed.
/// </summary>
public sealed class MutationLog
{
    private readonly WorldGenerator _gen;
    private readonly ContentDatabase _content;
    private readonly WorldSettings _settings;
    private readonly List<WorldMeta.MutationModel> _records = new();
    private volatile IMutationStep[] _steps = Array.Empty<IMutationStep>();

    public MutationLog(WorldGenerator gen, ContentDatabase content, WorldSettings settings)
    {
        _gen = gen;
        _content = content;
        _settings = settings;
    }

    /// <summary>Number of mutations so far (the level every chunk is brought up to).</summary>
    public int Level => _steps.Length;

    public IReadOnlyList<IMutationStep> Steps => _steps;

    public IReadOnlyList<string> Ids => _records.Select(r => r.Id).ToList();

    public bool Contains(string id) => _records.Any(r => r.Id == id);

    /// <summary>Zones created by mutations (the radiant reach, blights).</summary>
    public IEnumerable<IZoneStep> Zones => _steps.OfType<IZoneStep>();

    /// <summary>Appends a mutation (main thread). Its seed derives from the world seed and its id.</summary>
    public IMutationStep Append(WorldMutationDef def)
    {
        ulong seed = Hash.Combine((ulong)_settings.Seed, Hash.String("mutation:" + def.Id));
        var step = Create(def, seed);
        _records.Add(new WorldMeta.MutationModel(def.Id, seed));
        _steps = _steps.Append(step).ToArray();
        return step;
    }

    /// <summary>Restores the log from a save; mutations missing from content keep their slot but do nothing.</summary>
    public void Load(WorldMeta meta, ILog log)
    {
        var steps = new List<IMutationStep>();
        foreach (var m in meta.Mutations)
        {
            _records.Add(m);
            if (_content.Mutations.TryGet(m.Id, out var def)) steps.Add(Create(def, m.Seed));
            else
            {
                log.Warn("mutations", $"Saved world mutation '{m.Id}' is not in content; it no longer changes new chunks.");
                steps.Add(new EmptyStep());
            }
        }
        _steps = steps.ToArray();
    }

    public void Persist(WorldMeta meta)
    {
        meta.Mutations = _records.ToList();
        meta.MutationLevel = _records.Count;
    }

    /// <summary>
    /// Brings chunk data from level <paramref name="from"/> up to the snapshot's level; returns the same instance when
    /// nothing changed. Pure: safe on worker threads.
    /// </summary>
    public static ChunkData Apply(ChunkData data, Int3 coord, IReadOnlyList<IMutationStep> steps, int from)
    {
        if (from >= steps.Count) return data;
        if (data.IsUniform)
        {
            bool any = false;
            for (int i = from; i < steps.Count && !any; i++) any = steps[i].Affects(data.UniformBlock);
            if (!any) return data;
        }
        var blocks = data.Blocks?.ToArray() ?? Enumerable.Repeat(data.UniformBlock, ChunkMath.Volume).ToArray();
        var buf = new ChunkBuffer(blocks, ChunkMath.ChunkOrigin(coord));
        for (int i = from; i < steps.Count; i++) steps[i].Apply(ref buf, coord);
        return buf.Changed ? ChunkData.Compact(blocks, data.Shapes, data.Liquids, data.Flags) : data;
    }

    private IMutationStep Create(WorldMutationDef def, ulong seed) => def.Step.Type switch
    {
        MutationSpecs.OreVeins => new OreVeinsStep(_gen, _content, def.Step.Parse<OreVeinsMutationParams>(), seed),
        MutationSpecs.Wedge => Wedge(def.Step.Parse<WedgeMutationParams>(), seed),
        _ => throw new InvalidOperationException($"Unknown mutation step '{def.Step.Type}' (the content validator should have rejected it)."),
    };

    private WedgeStep Wedge(WedgeMutationParams p, ulong seed)
    {
        var id = p.Conversion ?? (p.ByEvil.TryGetValue(_settings.Evil.ToString().ToLowerInvariant(), out var byEvil) ? byEvil
            : throw new InvalidOperationException($"wedge has no conversion for evil '{_settings.Evil}'"));
        return new WedgeStep(_gen, _content, p, _content.Conversions.Get(id), seed);
    }
}
