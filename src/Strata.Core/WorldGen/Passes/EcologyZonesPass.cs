using System.Numerics;
using Strata.Core.Diagnostics;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 17 — enemy ecology zones: web-choked spider warrens in the caverns, and a summary of every
/// zone feature (dungeon, hives, mini-biomes, blights) that spawn rules and ambience query at runtime.
/// </summary>
public sealed class EcologyZonesPass : GenPass
{
    public override string Name => "ecology";
    public override int Number => 17;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("nests");
        var bands = ctx.Plan.Bands;
        for (int i = 0, n = Math.Max(1, ctx.Scaled(5)); i < n; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 80f, out float x, out float z)) continue;
            var center = new Vector3(x, bands.YForNormalizedDepth(rng.Range(0.4f, 0.9f)), z);
            ulong seed = rng.NextUInt();
            for (int lobe = 0; lobe < rng.Range(3, 6); lobe++)
            {
                var offset = new Vector3(rng.Range(-18f, 18f), rng.Range(-6f, 6f), rng.Range(-18f, 18f));
                float r = rng.Range(8f, 14f);
                ctx.Plan.AddFeature(new ChamberFeature(center + offset, new Vector3(r, r * 0.7f, r))
                {
                    Kind = "spider_nest", Chamber = ChamberKind.SpiderNest, Zone = "spider_nest", Seed = seed + (ulong)lobe,
                });
            }
        }

        int zones = ctx.Plan.Features.All.Count(f => f.Zone != null);
        ctx.Log.Debug("worldgen", $"Ecology: {zones} zone features registered.");
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is ChamberFeature ch && ch.Kind == "spider_nest") MiniBiomeCarver.Carve(ctx, ch);
    }
}
