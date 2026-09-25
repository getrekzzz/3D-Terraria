using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 2 — continents and coastline: a rounded-square continent with an irregular coast, continental
/// shelf and deep explorable ocean towards the world edge. Produces the base elevation that biomes refine.
/// </summary>
public sealed class ContinentsPass : GenPass
{
    public override string Name => "continents";
    public override int Number => 2;

    /// <summary>Normalized radius of the coastline (rounded-square metric).</summary>
    public const float Coast = 0.84f;

    public override void Plan(PlanContext ctx)
    {
        var plan = ctx.Plan;
        var seeds = ctx.SeedsFor(this);
        var coastNoise = NoiseField.Create(seeds, "coast", 220f, NoiseFractal.Fbm, 4);
        var seabed = NoiseField.Create(seeds, "seabed", 90f, NoiseFractal.Fbm, 3);
        float hx = plan.Size.X * 0.5f, hz = plan.Size.Z * 0.5f;
        int sea = plan.Bands.SeaLevel;
        float maxDepth = MathF.Min(90f, plan.Size.Y * 0.1f);

        plan.Landness.Fill((x, z) =>
        {
            float rs = RoundedRadius((x - hx) / hx, (z - hz) / hz) + coastNoise.Sample(x, z) * 0.06f;
            return DetMath.SmoothStep(Coast + 0.05f, Coast - 0.05f, rs);
        });
        plan.Height.Fill((x, z) =>
        {
            float rs = RoundedRadius((x - hx) / hx, (z - hz) / hz) + coastNoise.Sample(x, z) * 0.06f;
            float land = DetMath.SmoothStep(Coast + 0.05f, Coast - 0.05f, rs);
            float offshore = DetMath.SmoothStep(Coast - 0.02f, 1.02f, rs);
            float oceanFloor = sea - 6f - maxDepth * offshore + seabed.Sample(x, z) * 6f;
            float shore = sea + 2f + 5f * DetMath.SmoothStep(0.45f, 1f, land);
            return DetMath.Lerp(oceanFloor, shore, land);
        });
    }

    /// <summary>Rounded-square radius: 1 at the square's edge, corners pushed outward.</summary>
    public static float RoundedRadius(float u, float v)
    {
        float au = MathF.Abs(u), av = MathF.Abs(v);
        float euclid = MathF.Sqrt(u * u + v * v) * 0.86f;
        return MathF.Max(MathF.Max(au, av), euclid);
    }
}
