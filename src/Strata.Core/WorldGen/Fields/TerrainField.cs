using Strata.Core.Determinism;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Fields;

/// <summary>
/// Pure surface-shape functions: height with fine detail, and the near-surface 3D density. The surface is a heightfield
/// with a little 3D jitter (amplitude at most about 2 cells); only rare cliff zones (<see cref="Cliff"/>) get steep
/// walls and undercuts from the overhang noise.
/// </summary>
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
        _overhang = NoiseField.Create(seeds, "overhang", TerrainTuning.OverhangWavelength, NoiseFractal.Fbm, TerrainTuning.OverhangOctaves);
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

    /// <summary>0–1: cliff-zone strength at a column (0 almost everywhere).</summary>
    public float Cliff(float x, float z) => _plan.Cliffs.SampleSmooth(x, z);

    /// <summary>Amplitude (cells) of the 3D overhang noise for a column's ruggedness and cliff strength.</summary>
    public static float OverhangAmplitude(float ruggedness, float cliff) =>
        TerrainTuning.BaseAmp + ruggedness * TerrainTuning.RuggedAmp + cliff * TerrainTuning.CliffAmp;

    /// <summary>
    /// &gt; 0 means solid ground: the height below the surface plus 3D overhang noise of amplitude
    /// <paramref name="amplitude"/> (<see cref="OverhangAmplitude"/>) within amplitude + 1 cells of it. Below
    /// surface − amplitude every cell is solid, so caves measure their roof from there. In cliff zones the noise is
    /// terraced (steepened and clipped), giving flat shelves between steep walls with occasional undercuts.
    /// </summary>
    public float Density(int x, int y, int z, float surfaceHeight, float amplitude, float cliff)
    {
        float d = surfaceHeight - y;
        if (d <= -amplitude - 1f || d >= amplitude + 1f) return d;
        float n = _overhang.Sample(x, y * TerrainTuning.OverhangYStretch, z);
        if (cliff > TerrainTuning.TerraceLow)
        {
            float terraced = Math.Clamp(n * TerrainTuning.TerraceSharpness, -1f, 1f);
            n = DetMath.Lerp(n, terraced, DetMath.SmoothStep(TerrainTuning.TerraceLow, TerrainTuning.TerraceHigh, cliff));
        }
        return d + n * amplitude;
    }

    /// <summary>Thickness (cells) of the soil layer above rock at a column.</summary>
    public float SoilDepth(float x, float z) => 5f + _soil.Sample01(x, z) * 9f;
}
