namespace Strata.Core.WorldGen.Plan;

/// <summary>Coarse 2D float grid over the world's XZ plane with bilinear sampling (heightmaps, masks).</summary>
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
