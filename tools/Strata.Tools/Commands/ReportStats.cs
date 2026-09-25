using System.Collections.Concurrent;
using Strata.Core.Common;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Tools.Commands;

/// <summary>Aggregates block, liquid and object statistics over generated sample chunks.</summary>
internal sealed class ReportStats
{
    private readonly ConcurrentDictionary<string, long> _blocksByLayer = new();
    private readonly ConcurrentDictionary<string, long> _objects = new();
    private readonly WorldGenerator _gen;

    public ReportStats(WorldGenerator gen) => _gen = gen;

    public void Add(GeneratedChunk chunk)
    {
        var bands = _gen.Plan.Bands;
        for (int i = 0; i < ChunkMath.Volume; i++)
        {
            int y = chunk.Coord.Y * 32 + ChunkMath.FromIndex(i).Y;
            string layer = bands.LayerAt(y).ToString();
            string block = _gen.Content.Blocks[chunk.Data.GetBlock(i)].Id;
            _blocksByLayer.AddOrUpdate($"{layer}/{block}", 1, (_, v) => v + 1);
            byte liquid = chunk.Data.Liquids?[i] ?? 0;
            if (liquid != 0) _blocksByLayer.AddOrUpdate($"{layer}/liquid_{Strata.Core.Voxels.LiquidCode.Type(liquid)}", 1, (_, v) => v + 1);
        }
        foreach (var o in chunk.Objects) _objects.AddOrUpdate(o.Def.Id, 1, (_, v) => v + 1);
    }

    public object Summarize(WorldGenerator gen, long planMs, IEnumerable<double> timings)
    {
        var t = timings.ToList();
        var layers = _blocksByLayer.GroupBy(kv => kv.Key.Split('/')[0]).ToDictionary(
            g => g.Key,
            g =>
            {
                long total = g.Where(kv => !kv.Key.Contains("/liquid_")).Sum(kv => kv.Value);
                return g.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key.Split('/')[1], kv => Math.Round(100.0 * kv.Value / total, 3));
            });
        return new
        {
            seed = gen.Plan.Settings.Seed,
            size = gen.Plan.Settings.Size.ToString(),
            planHash = gen.Plan.Hash,
            planMs,
            chunks = t.Count,
            chunkMsAvg = Math.Round(t.Average(), 3),
            chunkMsP95 = Math.Round(t.OrderBy(v => v).ElementAt((int)(t.Count * 0.95)), 3),
            chunkMsMax = Math.Round(t.Max(), 3),
            oreChoices = gen.Plan.OreChoices,
            features = gen.Plan.Features.All.GroupBy(f => f.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            objects = _objects.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            percentByLayer = layers,
        };
    }
}
