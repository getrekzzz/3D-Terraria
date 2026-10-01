using Strata.Core.Determinism;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Fields;

/// <summary>Pure surface-shape functions: height with fine detail, and the near-surface 3D density.</summary>
public sealed class TerrainField
{
    private readonly WorldPlan _plan;
    private readonly NoiseField _detail;
    private readonly NoiseField _overhang;
    private readonly NoiseField _soil;

    public TerrainField(WorldPlan plan)
    {
        _plan = plan;
        var seeds = plan.Seeds.Child("terrain_field");
        _detail = NoiseField.Create(seeds, "detail", 40f, NoiseFractal.Fbm, 2);
        _overhang = NoiseField.Create(seeds, "overhang", 34f, NoiseFractal.Fbm, 3);
        _soil = NoiseField.Create(seeds, "soil", 48f, NoiseFractal.Fbm, 2);
    }

    /// <summary>
    /// Surface height at a column (cells, fractional): the plan height through a B-spline (no grid creases) plus gentle
    /// two-octave detail of at most 2 cells, broad enough that it never raises single-cell pimples.
    /// </summary>
    public float SurfaceHeight(float x, float z)
    {
        float land = _plan.Landness.Sample(x, z);
        return _plan.Height.SampleSmooth(x, z) + _detail.Sample(x, z) * (0.8f + 1.2f * land);
    }

    public float Ruggedness(float x, float z) => _plan.Ruggedness.SampleSmooth(x, z);

    /// <summary>Amplitude (cells) of 3D overhang noise for a ruggedness value.</summary>
    public static float OverhangAmplitude(float ruggedness) => 1.5f + ruggedness * 11f;

    /// <summary>&gt; 0 means solid ground. Adds 3D cliffs/overhangs close to the surface.</summary>
    public float Density(int x, int y, int z, float surfaceHeight, float ruggedness)
    {
        float d = surfaceHeight - y;
        float amp = OverhangAmplitude(ruggedness);
        if (d > -amp - 1f && d < amp + 1f) d += _overhang.Sample(x, y * 1.4f, z) * amp;
        return d;
    }

    /// <summary>Thickness (cells) of the soil layer above rock at a column.</summary>
    public float SoilDepth(float x, float z) => 5f + _soil.Sample01(x, z) * 9f;
}
