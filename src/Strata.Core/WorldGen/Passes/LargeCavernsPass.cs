using System.Numerics;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 6 — large underground spaces: grand caverns with rock pillars (landmark "what is that" spaces),
/// collapsed caverns full of rubble, flooded underground lakes, crystal geodes and mushroom hollows.
/// </summary>
public sealed class LargeCavernsPass : GenPass
{
    public override string Name => "caverns";
    public override int Number => 6;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("features");
        var plan = ctx.Plan;
        var bands = plan.Bands;
        ulong seed = ctx.SeedsFor(this).Value;
        int serial = 0;

        void Chamber(ChamberKind kind, float depthMin, float depthMax, float rh, float rv, float landness = 0.85f)
        {
            if (!ctx.TryRandomLandPoint(ref rng, landness, 40f, out float x, out float z)) return;
            float y = bands.YForNormalizedDepth(rng.Range(depthMin, depthMax));
            plan.AddFeature(new ChamberFeature(new Vector3(x, y, z), new Vector3(rh, rv, rh * rng.Range(0.7f, 1.3f)))
            {
                Kind = "cavern_" + kind.ToString().ToLowerInvariant(),
                Chamber = kind,
                Seed = seed + (ulong)serial++,
            });
        }

        for (int i = 0, n = Math.Max(1, ctx.Scaled(2)); i < n; i++) Chamber(ChamberKind.Grand, 0.5f, 0.78f, rng.Range(55f, 85f), rng.Range(24f, 36f));
        for (int i = 0, n = ctx.Scaled(5); i < n; i++) Chamber(ChamberKind.Collapsed, 0.3f, 0.85f, rng.Range(20f, 34f), rng.Range(12f, 18f));
        for (int i = 0, n = ctx.Scaled(8); i < n; i++) Chamber(ChamberKind.Lake, 0.2f, 0.85f, rng.Range(16f, 30f), rng.Range(8f, 13f));
        for (int i = 0, n = ctx.Scaled(10); i < n; i++) Chamber(ChamberKind.Geode, 0.45f, 0.95f, rng.Range(5f, 9f), rng.Range(5f, 9f));

        foreach (var region in plan.Regions.Where(r => r.Biome == "mushroom").ToList())
        {
            for (int i = 0; i < 4; i++)
            {
                float a = rng.NextFloat();
                float dist = rng.Range(0f, 0.6f) * region.RadiusX;
                float x = region.CenterX + Determinism.Trig.Cos(a) * dist, z = region.CenterZ + Determinism.Trig.Sin(a) * dist;
                float y = bands.YForNormalizedDepth(region.DepthCenter + rng.Range(-0.08f, 0.08f));
                float r = rng.Range(18f, 30f);
                plan.AddFeature(new ChamberFeature(new Vector3(x, y, z), new Vector3(r, r * 0.5f, r))
                {
                    Kind = "cavern_mushroom", Chamber = ChamberKind.Mushroom, Seed = seed + (ulong)serial++, Zone = "mushroom",
                });
            }
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is ChamberFeature ch && ch.Kind.StartsWith("cavern_", StringComparison.Ordinal))
                ChamberCarver.Carve(ctx, ch);
    }
}
