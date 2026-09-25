namespace Strata.Core.Determinism;

/// <summary>
/// Deterministic sine/cosine for world generation, measured in <em>turns</em> (1 turn = 360°).
/// Uses a Bhaskara-free minimax polynomial evaluated with basic arithmetic only, so results are
/// bit-identical on every platform.
/// </summary>
public static class Trig
{
    /// <summary>sin(2π·turns).</summary>
    public static float Sin(float turns)
    {
        // Reduce to [-0.5, 0.5)
        float x = turns - MathF.Floor(turns + 0.5f);
        // Map to [-0.25, 0.25] using symmetry sin(π - a) = sin(a)
        if (x > 0.25f) x = 0.5f - x;
        else if (x < -0.25f) x = -0.5f - x;
        // Polynomial in radians-scaled variable: sin(2πx), |x| <= 0.25 (7th-order Taylor-ish minimax)
        float a = x * 6.28318530718f;
        float a2 = a * a;
        return a * (1f + a2 * (-0.16666667f + a2 * (0.0083333310f + a2 * (-0.00019840874f + a2 * 2.7525562e-6f))));
    }

    /// <summary>cos(2π·turns).</summary>
    public static float Cos(float turns) => Sin(turns + 0.25f);
}
