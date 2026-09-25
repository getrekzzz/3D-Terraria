using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.World;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 4 — biome masks. Places biome regions following the reference layout (spawn forest in the
/// middle; tundra and jungle on opposite sides; desert to one flank with a buried desert beneath;
/// evil blights scattered; buried mushroom hollows), shapes surface relief per biome, then assigns a
/// biome to every cell and converts materials accordingly.
/// </summary>
public sealed class BiomeMasksPass : GenPass
{
    private BiomeRelief? _relief;
    private BiomeMaterials? _materials;

    public override string Name => "biomes";
    public override int Number => 4;

    public override void Plan(PlanContext ctx)
    {
        var seeds = ctx.SeedsFor(this);
        var rng = seeds.Rng("layout");
        var plan = ctx.Plan;
        float R = ctx.LandRadius;
        plan.SnowAngle = rng.NextFloat();

        Add(ctx, seeds, "tundra", 2, RegionProfile.DeepColumn, plan.SnowAngle + rng.Range(-0.03f, 0.03f), 0.5f * R, 0.2f * R, 0.25f * R);
        Add(ctx, seeds, "jungle", 3, RegionProfile.DeepColumn, plan.SnowAngle + 0.5f + rng.Range(-0.05f, 0.05f), 0.52f * R, 0.24f * R, 0.27f * R);
        float desertAngle = plan.SnowAngle + (rng.Chance(0.5f) ? 0.25f : -0.25f) + rng.Range(-0.04f, 0.04f);
        var desert = Add(ctx, seeds, "desert", 2, RegionProfile.ShallowColumn, desertAngle, 0.5f * R, 0.17f * R, 0.21f * R, depthCenter: 0.12f);
        Add(ctx, seeds, "underground_desert", 4, RegionProfile.Buried, 0, 0, desert.RadiusX * 1.05f, desert.RadiusZ * 1.05f,
            depthCenter: 0.34f, depthRadius: 0.27f, cx: desert.CenterX, cz: desert.CenterZ);

        string evil = plan.Settings.Evil == EvilKind.Umbral ? "umbral" : "sanguine";
        int evilCount = plan.Settings.Size switch { WorldSizePreset.Tiny => 1, WorldSizePreset.Small => 2, WorldSizePreset.Medium => 3, _ => 4 };
        var avoid = new List<float> { plan.SnowAngle, plan.SnowAngle + 0.5f, desertAngle };
        for (int i = 0; i < evilCount; i++)
        {
            float angle = PickAngle(ref rng, avoid, 0.09f);
            avoid.Add(angle);
            float size = rng.Range(0.085f, 0.12f) * R;
            Add(ctx, seeds, evil, 6, RegionProfile.ShallowColumn, angle, rng.Range(0.34f, 0.62f) * R, size, size * rng.Range(0.8f, 1.3f), depthCenter: 0.45f);
        }

        int mushrooms = Math.Max(1, ctx.Scaled(2));
        for (int i = 0; i < mushrooms; i++)
        {
            float size = rng.Range(0.06f, 0.09f) * R;
            Add(ctx, seeds, "mushroom", 5, RegionProfile.Buried, rng.NextFloat(), rng.Range(0.2f, 0.7f) * R, size, size, depthCenter: rng.Range(0.5f, 0.75f), depthRadius: 0.16f);
        }

        plan.Regions.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        _relief = new BiomeRelief(seeds, plan);
        _relief.Apply(ctx);
        _materials = new BiomeMaterials(seeds, ctx.Content);

        int cx = (int)ctx.CenterX, cz = (int)ctx.CenterZ;
        plan.Spawn = new Int3(cx, (int)MathF.Ceiling(ctx.Terrain.SurfaceHeight(cx, cz)) + 1, cz);
    }

    public override void Generate(ChunkGenContext ctx)
    {
        FillBiomeGrid(ctx);
        if (!ctx.AllAir) _materials!.Apply(ctx);
    }

    /// <summary>Biome per padded cell; regions evaluated per column at 4-cell vertical steps.</summary>
    private static void FillBiomeGrid(ChunkGenContext ctx)
    {
        var field = ctx.Gen.Biomes;
        int size = ChunkGenContext.Size;
        int samples = (size + 3) / 4;
        Span<float> ys = stackalloc float[samples];
        Span<byte> result = stackalloc byte[samples];
        for (int k = 0; k < samples; k++) ys[k] = ctx.Origin.Y + k * 4 + 2;
        for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                float h = ctx.SurfaceHeight[ChunkGenContext.Column(lx, lz)];
                field.BiomeColumn(ctx.Origin.X + lx, ctx.Origin.Z + lz, h, ys, result);
                for (int ly = 0; ly < size; ly++) ctx.Biome[ChunkGenContext.Index(lx, ly, lz)] = result[ly >> 2];
            }
    }

    private static BiomeRegion Add(PlanContext ctx, SeedTree seeds, string biome, int priority, RegionProfile profile,
        float angle, float distance, float rx, float rz, float depthCenter = 1f, float depthRadius = 0.3f, float? cx = null, float? cz = null)
    {
        var region = new BiomeRegion
        {
            Biome = biome,
            BiomeId = (byte)ctx.Content.Biomes.Get(biome).RuntimeId,
            Priority = priority,
            Profile = profile,
            CenterX = cx ?? ctx.CenterX + Trig.Cos(angle) * distance,
            CenterZ = cz ?? ctx.CenterZ + Trig.Sin(angle) * distance,
            RadiusX = rx,
            RadiusZ = rz,
            Angle = angle,
            DepthCenter = depthCenter,
            DepthRadius = depthRadius,
            Edge = NoiseField.Create(seeds, $"edge_{ctx.Plan.Regions.Count}", MathF.Max(rx, rz) * 0.9f, NoiseFractal.Fbm, 3),
        };
        ctx.Plan.Regions.Add(region);
        return region;
    }

    private static float PickAngle(ref Pcg32 rng, List<float> avoid, float minGap)
    {
        float best = rng.NextFloat();
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float candidate = rng.NextFloat();
            if (avoid.All(a => AngularGap(a, candidate) >= minGap)) return candidate;
            best = candidate;
        }
        return best;
    }

    private static float AngularGap(float a, float b)
    {
        float d = MathF.Abs(a - b) % 1f;
        return MathF.Min(d, 1f - d);
    }
}
