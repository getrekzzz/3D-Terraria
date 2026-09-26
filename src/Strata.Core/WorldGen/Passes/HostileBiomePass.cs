using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 12 — hostile (evil) biomes: jagged zig-zag chasms plunging from the blighted surface into the
/// underground, lined with evil stone and ending in chambers (where heart/orb objectives will live).
/// </summary>
public sealed class HostileBiomePass : GenPass
{
    public override string Name => "hostile";
    public override int Number => 12;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("chasms");
        var bands = ctx.Plan.Bands;
        foreach (var region in ctx.Plan.Regions.Where(r => r.Biome is "corruption" or "crimson").ToList())
        {
            string lining = region.Biome == "corruption" ? "ebonstone" : "crimstone";
            int chasms = rng.Range(3, 6);
            for (int c = 0; c < chasms; c++)
            {
                float a = rng.NextFloat(), d = rng.Range(0f, 0.7f) * region.RadiusX;
                float x = region.CenterX + Trig.Cos(a) * d, z = region.CenterZ + Trig.Sin(a) * d;
                var p = new Vector3(x, ctx.Terrain.SurfaceHeight(x, z) + 4f, z);
                float bottom = bands.YForNormalizedDepth(rng.Range(0.22f, 0.42f));
                float r = rng.Range(2.6f, 3.8f);
                while (p.Y > bottom)
                {
                    var next = p + new Vector3(rng.Range(-6f, 6f), -rng.Range(8f, 14f), rng.Range(-6f, 6f));
                    ctx.Plan.AddFeature(new CapsuleFeature(p, next, r, r) { Kind = "chasm", Lining = lining });
                    p = next;
                }
                ctx.Plan.AddFeature(new ChamberFeature(p, new Vector3(7, 5, 7)) { Kind = "evil_heart_chamber", Chamber = ChamberKind.Cavern, Zone = region.Biome, Seed = rng.NextUInt() });
            }
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is CapsuleFeature c && c.Kind == "chasm") FeatureCarver.Capsule(ctx, c, ctx.Palette.Id(c.Lining!));
            else if (f is ChamberFeature ch && ch.Kind == "evil_heart_chamber") ChamberCarver.Carve(ctx, ch);
        }
    }
}
