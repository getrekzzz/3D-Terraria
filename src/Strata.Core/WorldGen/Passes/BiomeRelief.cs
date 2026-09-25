using Strata.Core.Determinism;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Passes;

/// <summary>Per-biome surface relief (hills, mountains, dunes, jungle ruggedness) blended across borders.</summary>
internal sealed class BiomeRelief
{
    private readonly WorldPlan _plan;
    private readonly NoiseField _hills, _ridges, _dunes, _jagged, _mountainMask, _mountains;

    public BiomeRelief(SeedTree seeds, WorldPlan plan)
    {
        _plan = plan;
        _hills = NoiseField.Create(seeds, "hills", 190f, NoiseFractal.Fbm, 4);
        _ridges = NoiseField.Create(seeds, "ridges", 240f, NoiseFractal.Ridged, 4);
        _dunes = NoiseField.Create(seeds, "dunes", 70f, NoiseFractal.Ridged, 2);
        _jagged = NoiseField.Create(seeds, "jagged", 80f, NoiseFractal.Ridged, 3);
        _mountainMask = NoiseField.Create(seeds, "mountain_mask", 700f, NoiseFractal.Fbm, 2);
        _mountains = NoiseField.Create(seeds, "mountains", 200f, NoiseFractal.Ridged, 5);
    }

    public void Apply(PlanContext ctx)
    {
        var plan = _plan;
        var regions = plan.Regions;
        var weights = new float[regions.Count];
        float cx = ctx.CenterX, cz = ctx.CenterZ;
        var relief = new Grid2D(plan.Size.X, plan.Size.Z, WorldPlan.HeightStep);
        var rugged = new Grid2D(plan.Size.X, plan.Size.Z, WorldPlan.HeightStep);

        for (int gz = 0; gz < relief.Depth; gz++)
            for (int gx = 0; gx < relief.Width; gx++)
            {
                float x = gx * relief.Step, z = gz * relief.Step;
                float total = 0f;
                for (int i = 0; i < regions.Count; i++)
                {
                    var r = regions[i];
                    weights[i] = r.Profile == RegionProfile.Buried ? 0f : DetMath.Clamp01(r.Membership(x, z, 0f) * 3f);
                    total += weights[i];
                }
                float forest = MathF.Max(0f, 1f - total);
                float norm = forest + total;
                float h = forest * ForestRelief(x, z), rug = forest * 0.15f;
                for (int i = 0; i < regions.Count; i++)
                {
                    if (weights[i] <= 0f) continue;
                    h += weights[i] * Relief(regions[i].Biome, x, z);
                    rug += weights[i] * Rugged(regions[i].Biome);
                }
                h /= norm;
                rug /= norm;

                float dx = x - cx, dz = z - cz;
                float spawnCalm = DetMath.SmoothStep(30f, 140f, MathF.Sqrt(dx * dx + dz * dz));
                float mask = DetMath.SmoothStep(0.25f, 0.55f, _mountainMask.Sample(x, z)) * spawnCalm * forest;
                h += mask * (30f + 55f * _mountains.Sample01(x, z));
                rug = DetMath.Lerp(rug, 0.8f, mask);

                relief[gx, gz] = h * DetMath.Lerp(0.35f, 1f, spawnCalm);
                rugged[gx, gz] = rug * spawnCalm;
            }

        relief.Blur(1);
        for (int i = 0; i < plan.Height.Values.Length; i++)
        {
            float land = plan.Landness.Values[i];
            float beach = DetMath.SmoothStep(0.45f, 0.85f, land);
            plan.Height.Values[i] += relief.Values[i] * land * beach;
            plan.Ruggedness.Values[i] = rugged.Values[i] * land;
        }
    }

    private float ForestRelief(float x, float z) => 7f + 24f * _hills.Sample01(x, z) * _hills.Sample01(z * 0.7f, x * 0.7f);

    private float Relief(string biome, float x, float z) => biome switch
    {
        "tundra" => 10f + 48f * _ridges.Sample01(x, z) + 6f * _hills.Sample(x, z),
        "jungle" => 12f + 22f * _hills.Sample01(x, z) + 10f * _jagged.Sample01(x, z),
        "desert" => 4f + 7f * _dunes.Sample01(x, z),
        "umbral" or "sanguine" => 9f + 14f * _hills.Sample01(x, z) + 12f * _jagged.Sample01(x, z),
        _ => ForestRelief(x, z),
    };

    private static float Rugged(string biome) => biome switch
    {
        "tundra" => 0.55f,
        "jungle" => 0.6f,
        "desert" => 0.05f,
        "umbral" or "sanguine" => 0.75f,
        _ => 0.15f,
    };
}
