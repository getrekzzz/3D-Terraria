using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 7 — underground rivers (long, wide, low channels half-full of water) and deep lava tubes lined
/// with obsidian crust, half-filled with lava.
/// </summary>
public sealed class UndergroundRiversPass : GenPass
{
    public override string Name => "rivers";
    public override int Number => 7;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("features");
        for (int i = 0, n = Math.Max(1, ctx.Scaled(5)); i < n; i++)
            Channel(ctx, ref rng, "river", 0.12f, 0.45f, 4f, 6.5f, 0.55f, LiquidType.Water, 0.55f, null);
        for (int i = 0, n = Math.Max(1, ctx.Scaled(5)); i < n; i++)
            Channel(ctx, ref rng, "lava_tube", 0.82f, 0.97f, 4.5f, 7f, 0.7f, LiquidType.Lava, 0.35f, "obsidian");
    }

    private static void Channel(PlanContext ctx, ref Pcg32 rng, string kind, float dMin, float dMax, float rMin, float rMax,
        float vScale, LiquidType fill, float fraction, string? lining)
    {
        if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 0f, out float x, out float z)) return;
        var bands = ctx.Plan.Bands;
        float y = bands.YForNormalizedDepth(rng.Range(dMin, dMax));
        float heading = rng.NextFloat();
        int segments = rng.Range(24, 60);
        var p = new Vector3(x, y, z);
        float r = rng.Range(rMin, rMax);
        for (int s = 0; s < segments; s++)
        {
            heading += rng.Range(-0.05f, 0.05f);
            float len = rng.Range(8f, 14f);
            var next = p + new Vector3(Trig.Cos(heading) * len, rng.Range(-1.2f, 1.2f), Trig.Sin(heading) * len);
            if (ctx.Plan.Landness.Sample(next.X, next.Z) < 0.85f) break;
            float nr = Math.Clamp(r + rng.Range(-0.8f, 0.8f), rMin, rMax);
            ctx.Plan.AddFeature(new CapsuleFeature(p, next, r, nr, vScale) { Kind = kind, Fill = fill, FillFraction = fraction, Lining = lining });
            p = next;
            r = nr;
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is CapsuleFeature c && c.Kind is "river" or "lava_tube")
                FeatureCarver.Capsule(ctx, c, c.Lining != null ? ctx.Palette.Id(c.Lining) : (ushort)0);
    }
}
