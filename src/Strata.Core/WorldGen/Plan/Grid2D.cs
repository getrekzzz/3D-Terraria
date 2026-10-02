namespace Strata.Core.WorldGen.Plan;

/// <summary>Coarse 2D float grid over the world's XZ plane with bilinear (masks) and B-spline (heightmaps) sampling.</summary>
public sealed class Grid2D
{
    public Grid2D(int worldWidth, int worldDepth, int step)
    {
        Step = step;
        Width = worldWidth / step + 1;
        Depth = worldDepth / step + 1;
        Values = new float[Width * Depth];
    }

    public int Step { get; }
    public int Width { get; }
    public int Depth { get; }
    public float[] Values { get; }

    public float this[int gx, int gz]
    {
        get => Values[Math.Clamp(gz, 0, Depth - 1) * Width + Math.Clamp(gx, 0, Width - 1)];
        set => Values[gz * Width + gx] = value;
    }

    /// <summary>Bilinear sample at a world-cell position.</summary>
    public float Sample(float x, float z)
    {
        float fx = x / Step, fz = z / Step;
        int gx = (int)MathF.Floor(fx), gz = (int)MathF.Floor(fz);
        float tx = fx - gx, tz = fz - gz;
        float a = this[gx, gz], b = this[gx + 1, gz], c = this[gx, gz + 1], d = this[gx + 1, gz + 1];
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    /// <summary>
    /// Uniform cubic B-spline sample at a world-cell position over the 4×4 surrounding samples (clamped at the edges):
    /// smooth in value and slope across grid lines, so heightmaps show no facets or creases along the grid. It
    /// approximates rather than interpolates (a lone peak sample is softened) and never leaves the 4×4 range.
    /// </summary>
    public float SampleSmooth(float x, float z)
    {
        float fx = x / Step, fz = z / Step;
        int gx = (int)MathF.Floor(fx), gz = (int)MathF.Floor(fz);
        BSplineWeights(fx - gx, out float wx0, out float wx1, out float wx2, out float wx3);
        BSplineWeights(fz - gz, out float wz0, out float wz1, out float wz2, out float wz3);
        int x0 = Math.Clamp(gx - 1, 0, Width - 1), x1 = Math.Clamp(gx, 0, Width - 1);
        int x2 = Math.Clamp(gx + 1, 0, Width - 1), x3 = Math.Clamp(gx + 2, 0, Width - 1);
        float sum = 0f;
        for (int k = 0; k < 4; k++)
        {
            int r = Math.Clamp(gz - 1 + k, 0, Depth - 1) * Width;
            float row = wx0 * Values[r + x0] + wx1 * Values[r + x1] + wx2 * Values[r + x2] + wx3 * Values[r + x3];
            sum += row * (k == 0 ? wz0 : k == 1 ? wz1 : k == 2 ? wz2 : wz3);
        }
        return sum;
    }

    /// <summary>Uniform cubic B-spline basis at fraction t (non-negative, sums to 1).</summary>
    private static void BSplineWeights(float t, out float w0, out float w1, out float w2, out float w3)
    {
        float t2 = t * t, t3 = t2 * t, u = 1f - t;
        w0 = u * u * u / 6f;
        w1 = (3f * t3 - 6f * t2 + 4f) / 6f;
        w2 = (-3f * t3 + 3f * t2 + 3f * t + 1f) / 6f;
        w3 = t3 / 6f;
    }

    public void Fill(Func<int, int, float> fn)
    {
        for (int gz = 0; gz < Depth; gz++)
            for (int gx = 0; gx < Width; gx++)
                Values[gz * Width + gx] = fn(gx * Step, gz * Step);
    }

    /// <summary>Box blur in place (radius in grid samples), used to soften biome transitions.</summary>
    public void Blur(int radius)
    {
        if (radius <= 0) return;
        var tmp = new float[Values.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            bool horizontal = pass == 0;
            for (int gz = 0; gz < Depth; gz++)
                for (int gx = 0; gx < Width; gx++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        sum += horizontal ? this[gx + k, gz] : this[gx, gz + k];
                        n++;
                    }
                    tmp[gz * Width + gx] = sum / n;
                }
            Array.Copy(tmp, Values, Values.Length);
        }
    }
}
