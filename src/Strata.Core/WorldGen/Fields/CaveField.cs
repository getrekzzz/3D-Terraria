using Strata.Core.Determinism;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Fields;

/// <summary>
/// Noise-driven cave systems: "spaghetti" tunnels (intersection of two noise zero-sets), "cheese"
/// caverns (large blobs, deeper = larger) and thin vertical cracks. Evaluated through lattices for speed.
/// </summary>
public sealed class CaveField
{
    private readonly WorldPlan _plan;
    private readonly NoiseField _spagA, _spagB, _cheese, _pillars, _region, _crack, _crackMask;

    public CaveField(WorldPlan plan)
    {
        _plan = plan;
        var s = plan.Seeds.Child("cave_field");
        _spagA = NoiseField.Create(s, "spaghetti_a", 96f, NoiseFractal.Fbm, 2, 0.3f);
        _spagB = NoiseField.Create(s, "spaghetti_b", 96f, NoiseFractal.Fbm, 2, 0.3f);
        _cheese = NoiseField.Create(s, "cheese", 120f, NoiseFractal.Fbm, 3, 0.5f);
        _pillars = NoiseField.Create(s, "pillars", 22f, NoiseFractal.None, 1);
        _region = NoiseField.Create(s, "region", 420f, NoiseFractal.Fbm, 2);
        _crack = NoiseField.Create(s, "crack", 46f, NoiseFractal.None, 1);
        _crackMask = NoiseField.Create(s, "crack_mask", 30f, NoiseFractal.Fbm, 2);
    }

    public float SpaghettiA(int x, int y, int z) => _spagA.Sample(x, y * 1.25f, z);
    public float SpaghettiB(int x, int y, int z) => _spagB.Sample(x, y * 1.25f, z);
    public float Cheese(int x, int y, int z) => _cheese.Sample(x, y * 1.7f, z) - _pillars.Sample(x, y * 0.3f, z) * 0.08f;

    /// <summary>Regional cave abundance 0–1 (some areas are riddled, others solid).</summary>
    public float Abundance(float x, float z) => _region.Sample01(x, z);

    /// <summary>
    /// Decides whether noise caves carve a cell, given lattice-sampled noise values.
    /// <paramref name="surfaceDepth"/> is cells below the local surface; <paramref name="depth"/> is normalized depth.
    /// </summary>
    public bool Carves(float spagA, float spagB, float cheese, float depth, float surfaceDepth, float abundance)
    {
        if (surfaceDepth < 5f) return false;
        float nearSurface = DetMath.SmoothStep(5f, 22f, surfaceDepth);

        float t = (0.1f + 0.045f * DetMath.Clamp01(depth * 1.4f)) * (0.75f + 0.5f * abundance) * nearSurface;
        if (spagA * spagA + spagB * spagB < t * t) return true;

        if (depth > 0.12f)
        {
            float threshold = 0.36f - 0.2f * DetMath.SmoothStep(0.2f, 0.85f, depth) - 0.1f * abundance;
            if (cheese > threshold) return true;
        }
        return false;
    }

    /// <summary>Column test for thin vertical fissures (cheap 2D part).</summary>
    public bool CrackColumn(int x, int z) => MathF.Abs(_crack.Sample(x, z)) < 0.012f;

    /// <summary>Thin vertical fissures in the upper underground (call only where <see cref="CrackColumn"/> is true).</summary>
    public bool Crack(int x, int y, int z, float depth)
    {
        if (depth < 0.02f || depth > 0.45f) return false;
        return _crackMask.Sample(x * 0.5f, y, z * 0.5f) > 0.4f;
    }
}
