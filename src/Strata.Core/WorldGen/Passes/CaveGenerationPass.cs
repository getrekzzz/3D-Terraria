using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Fields;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 5 — cave networks: noise tunnels and cracks everywhere underground, plus planned features that
/// connect the surface to the depths: meandering worm tunnels (cave entrances), vertical shafts and
/// sinkholes that drop into chambers and let sunlight into the caves.
/// </summary>
public sealed class CaveGenerationPass : GenPass
{
    public override string Name => "caves";
    public override int Number => 5;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("features");
        var plan = ctx.Plan;
        int worms = ctx.Scaled(190), shafts = ctx.Scaled(16), sinkholes = ctx.Scaled(7);
        for (int i = 0; i < worms; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 48f, out float x, out float z)) continue;
            AddWorm(ctx, ref rng, new Vector3(x, ctx.Terrain.SurfaceHeight(x, z) + 2f, z));
        }
        for (int i = 0; i < shafts; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 60f, out float x, out float z)) continue;
            float top = ctx.Terrain.SurfaceHeight(x, z) + 3f;
            float depth = rng.Range(60f, 200f);
            var a = new Vector3(x, top, z);
            float r = rng.Range(2.6f, 4.2f);
            int segments = (int)(depth / 16f) + 1;
            for (int s = 0; s < segments; s++)
            {
                var b = a + new Vector3(rng.Range(-3f, 3f), -depth / segments, rng.Range(-3f, 3f));
                plan.AddFeature(new CapsuleFeature(a, b, r, r) { Kind = "shaft" });
                a = b;
            }
        }
        for (int i = 0; i < sinkholes; i++)
        {
            if (!ctx.TryRandomLandPoint(ref rng, 0.92f, 90f, out float x, out float z)) continue;
            float top = ctx.Terrain.SurfaceHeight(x, z) + 6f;
            float depth = rng.Range(40f, 90f);
            float topRadius = rng.Range(10f, 18f);
            plan.AddFeature(new ConeFeature(new Vector3(x, top, z), topRadius, rng.Range(4f, 6f), depth) { Kind = "sinkhole" });
            float cr = rng.Range(14f, 24f);
            plan.AddFeature(new ChamberFeature(new Vector3(x, top - depth - cr * 0.3f, z), new Vector3(cr, cr * 0.55f, cr)) { Kind = "sinkhole_chamber", Chamber = ChamberKind.Cavern });
        }
    }

    private static void AddWorm(PlanContext ctx, ref Pcg32 rng, Vector3 start)
    {
        float heading = rng.NextFloat();
        float pitch = rng.Range(-0.12f, -0.06f);
        int segments = rng.Range(14, 42);
        float radius = rng.Range(2.6f, 4.4f);
        var p = start;
        float floor = ctx.Plan.Bands.YForNormalizedDepth(rng.Range(0.25f, 0.8f));
        for (int s = 0; s < segments && p.Y > floor; s++)
        {
            heading += rng.Range(-0.07f, 0.07f);
            pitch = Math.Clamp(pitch + rng.Range(-0.03f, 0.03f), -0.2f, -0.02f);
            float len = rng.Range(6f, 10f);
            float horizontal = Trig.Cos(pitch) * len;
            var next = p + new Vector3(Trig.Cos(heading) * horizontal, Trig.Sin(pitch) * len, Trig.Sin(heading) * horizontal);
            float nr = Math.Clamp(radius + rng.Range(-0.6f, 0.6f), 2.4f, 5.2f);
            ctx.Plan.AddFeature(new CapsuleFeature(p, next, radius, nr) { Kind = "worm" });
            p = next;
            radius = nr;
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        CarveNoiseCaves(ctx);
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            switch (f)
            {
                case CapsuleFeature c when c.Kind is "worm" or "shaft":
                    FeatureCarver.Capsule(ctx, c);
                    break;
                case ConeFeature cone:
                    CarveCone(ctx, cone);
                    break;
                case ChamberFeature ch when ch.Kind == "sinkhole_chamber":
                    ChamberCarver.Carve(ctx, ch);
                    break;
            }
        }
    }

    private static void CarveNoiseCaves(ChunkGenContext ctx)
    {
        var caves = ctx.Gen.Caves;
        var p = ctx.Palette;
        var size = Int3.One * ChunkGenContext.Size;
        var spagA = Dense(ctx, new LatticeSampler(ctx.Origin, size, caves.SpaghettiA));
        var spagB = Dense(ctx, new LatticeSampler(ctx.Origin, size, caves.SpaghettiB));
        // Cheese caverns only exist below depth 0.12; the box bottom is its deepest cell.
        bool deep = ctx.Depth(ctx.Origin.Y) > 0.12f;
        var cheese = deep ? Dense(ctx, new LatticeSampler(ctx.Origin, size, caves.Cheese, 8)) : null;
        var spawn = ctx.Plan.Spawn;
        int n = ChunkGenContext.Size;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                float h = ctx.SurfaceHeight[ChunkGenContext.Column(lx, lz)];
                float abundance = caves.Abundance(wx, wz);
                float sdx = wx - spawn.X, sdz = wz - spawn.Z;
                float spawnGuard = sdx * sdx + sdz * sdz < 40f * 40f ? 18f : 0f;
                bool crackColumn = caves.CrackColumn(wx, wz);
                for (int ly = 0; ly < n; ly++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    ushort b = ctx.Blocks[i];
                    if (b == p.Air || b == p.Bedrock) continue;
                    int wy = ctx.Origin.Y + ly;
                    if (wy < ctx.Plan.Bands.UnderworldTop + 6) continue;
                    float sd = h - wy - spawnGuard;
                    float depth = ctx.Depth(wy);
                    if (caves.Carves(spagA[i], spagB[i], cheese?[i] ?? -1f, depth, sd, abundance)
                        || (crackColumn && sd > 8f && caves.Crack(wx, wy, wz, depth)))
                        ctx.Blocks[i] = p.Air;
                }
            }
    }

    private static float[] Dense(ChunkGenContext ctx, LatticeSampler sampler)
    {
        var dest = new float[ChunkGenContext.Volume];
        sampler.FillDense(dest, ChunkGenContext.Size);
        return dest;
    }

    private static void CarveCone(ChunkGenContext ctx, ConeFeature cone)
    {
        FeatureCarver.ForEachCell(ctx, cone.Bounds, (x, y, z, i) =>
        {
            float t = DetMath.Clamp01((cone.Top.Y - y) / cone.Depth);
            if (y > cone.Top.Y + 20f || cone.Top.Y - y > cone.Depth + 2f) return;
            float r = y > cone.Top.Y ? cone.TopRadius * 1.15f : DetMath.Lerp(cone.TopRadius, cone.BottomRadius, t * t);
            float dx = x + 0.5f - cone.Top.X, dz = z + 0.5f - cone.Top.Z;
            if (dx * dx + dz * dz < r * r && ctx.Blocks[i] != ctx.Palette.Bedrock) ctx.Blocks[i] = ctx.Palette.Air;
        });
    }
}
