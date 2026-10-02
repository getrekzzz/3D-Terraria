using Strata.Core.Determinism;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.Tests.WorldGen;

/// <summary>The plan grids' cubic B-spline sampling (heights without grid creases) and the soft ridge shape.</summary>
public class Grid2DTests
{
    private const int Step = 8;

    private static Grid2D Grid(Func<int, int, float> fn)
    {
        var g = new Grid2D(128, 96, Step);
        for (int gz = 0; gz < g.Depth; gz++)
            for (int gx = 0; gx < g.Width; gx++)
                g[gx, gz] = fn(gx, gz);
        return g;
    }

    private static Grid2D RandomGrid(int seed)
    {
        var rng = new Random(seed);
        return Grid((_, _) => (float)rng.NextDouble() * 10f);
    }

    [Fact]
    public void ReproducesConstantGridsEverywhere()
    {
        var g = Grid((_, _) => 3.25f);
        for (float z = -10f; z <= 106f; z += 1.7f)
            for (float x = -10f; x <= 138f; x += 2.3f)
                Assert.InRange(g.SampleSmooth(x, z), 3.25f - 1e-4f, 3.25f + 1e-4f);
    }

    [Fact]
    public void ReproducesLinearGridsInTheInterior()
    {
        var g = Grid((gx, gz) => 0.7f * gx - 1.3f * gz + 5f);
        // All four taps must lie inside the grid: one sample in from each edge (plus the far tap).
        for (float z = Step; z < (g.Depth - 2) * Step; z += 1.3f)
            for (float x = Step; x < (g.Width - 2) * Step; x += 1.1f)
            {
                float expected = 0.7f * x / Step - 1.3f * z / Step + 5f;
                Assert.InRange(g.SampleSmooth(x, z), expected - 1e-4f, expected + 1e-4f);
            }
    }

    [Fact]
    public void ValueAndSlopeAreContinuousAcrossGridLines()
    {
        var g = RandomGrid(7);
        const float h = 0.01f;
        float worstValue = 0f, worstSlope = 0f, worstBilinearSlope = 0f;
        for (int k = 2; k < g.Width - 3; k++)
            for (float z = 9.5f; z < 80f; z += 7.3f)
            {
                float line = k * Step;
                float left = g.SampleSmooth(line - 1e-3f, z), right = g.SampleSmooth(line + 1e-3f, z);
                worstValue = MathF.Max(worstValue, MathF.Abs(right - left));
                worstSlope = MathF.Max(worstSlope, MathF.Abs(SlopeJump(g.SampleSmooth, line, z, h)));
                worstBilinearSlope = MathF.Max(worstBilinearSlope, MathF.Abs(SlopeJump(g.Sample, line, z, h)));
            }
        for (int k = 2; k < g.Depth - 3; k++)
            for (float x = 11.5f; x < 110f; x += 6.1f)
            {
                float line = k * Step;
                worstSlope = MathF.Max(worstSlope, MathF.Abs(SlopeJump((a, b) => g.SampleSmooth(b, a), line, x, h)));
            }
        Assert.True(worstValue < 0.01f, $"value jumps by {worstValue} across a grid line");
        Assert.True(worstSlope < 0.01f, $"slope jumps by {worstSlope} across a grid line");
        // The same probe sees the creases of bilinear sampling, so it is sensitive enough.
        Assert.True(worstBilinearSlope > 0.1f, $"bilinear slope jump only {worstBilinearSlope}");
    }

    /// <summary>Right minus left one-sided difference along x at a grid line.</summary>
    private static float SlopeJump(Func<float, float, float> f, float line, float z, float h) =>
        (f(line + h, z) - f(line, z)) / h - (f(line, z) - f(line - h, z)) / h;

    [Fact]
    public void StaysWithinTheSurroundingSamples()
    {
        var g = RandomGrid(11);
        var rng = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            float x = (float)rng.NextDouble() * 140f - 6f, z = (float)rng.NextDouble() * 108f - 6f;
            int gx = (int)MathF.Floor(x / Step), gz = (int)MathF.Floor(z / Step);
            float min = float.MaxValue, max = float.MinValue;
            for (int dz = -1; dz <= 2; dz++)
                for (int dx = -1; dx <= 2; dx++)
                {
                    float v = g[gx + dx, gz + dz];
                    min = MathF.Min(min, v);
                    max = MathF.Max(max, v);
                }
            Assert.InRange(g.SampleSmooth(x, z), min - 1e-4f, max + 1e-4f);
        }
    }

    [Fact]
    public void SoftRidgeHasARoundedCrestAndFallsToZero()
    {
        Assert.Equal(1f, DetMath.SoftRidge(0f, 0.25f), 5);
        Assert.Equal(0f, DetMath.SoftRidge(1f, 0.25f), 5);
        Assert.Equal(0f, DetMath.SoftRidge(-1f, 0.25f), 5);
        Assert.Equal(DetMath.SoftRidge(0.4f, 0.25f), DetMath.SoftRidge(-0.4f, 0.25f), 6);
        // Rounded: the slope near the crest is small (a plain 1 − |n| ridge has slope 1 on both sides).
        float nearCrest = (DetMath.SoftRidge(0f, 0.25f) - DetMath.SoftRidge(0.02f, 0.25f)) / 0.02f;
        Assert.InRange(nearCrest, 0f, 0.15f);
        for (float n = -1f; n < 1f; n += 0.05f)
            Assert.True(DetMath.SoftRidge(MathF.Abs(n) + 0.05f, 0.25f) < DetMath.SoftRidge(MathF.Abs(n), 0.25f), "falls away from the crest");
    }
}
