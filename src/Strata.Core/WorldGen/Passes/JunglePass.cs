using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 11 — jungle specifics: honey-filled hives buried in the jungle column (Hive Matriarch lairs) and
/// the reserved site of the jungle temple (built when temple progression is implemented).
/// </summary>
public sealed class JunglePass : GenPass
{
    public override string Name => "jungle";
    public override int Number => 11;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("features");
        var bands = ctx.Plan.Bands;
        foreach (var jungle in ctx.Plan.Regions.Where(r => r.Biome == "jungle").ToList())
        {
            int hives = Math.Max(1, ctx.Scaled(2));
            for (int i = 0; i < hives; i++)
            {
                float a = rng.NextFloat(), d = rng.Range(0f, 0.55f) * jungle.RadiusX;
                var center = new Vector3(jungle.CenterX + Trig.Cos(a) * d, bands.YForNormalizedDepth(rng.Range(0.18f, 0.6f)), jungle.CenterZ + Trig.Sin(a) * d);
                float r = rng.Range(16f, 24f);
                ctx.Plan.AddFeature(new ChamberFeature(center, new Vector3(r, r * 0.8f, r)) { Kind = "hive", Chamber = ChamberKind.Hive, Zone = "hive", Seed = rng.NextUInt() });
            }

            var site = new Vector3(jungle.CenterX, bands.YForNormalizedDepth(0.68f), jungle.CenterZ);
            ctx.Plan.Features.Add(new ChamberFeature(site, new Vector3(40, 20, 40)) { Kind = "temple_site", Chamber = ChamberKind.Cavern });
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is ChamberFeature ch && ch.Kind == "hive") MiniBiomeCarver.Carve(ctx, ch);
    }
}
