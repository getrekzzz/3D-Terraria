namespace Strata.Core.Presentation;

/// <summary>The colour-vision deficiencies the colour-blind setting corrects for (X-12).</summary>
public enum ColorBlindMode { Off, Protanopia, Deuteranopia, Tritanopia }

/// <summary>
/// Colour-blind correction by daltonisation (X-12). A deficiency is simulated with Machado, Oliveira and Fernandes'
/// (2009) matrices on linear RGB; what the viewer loses (original − simulated) is moved onto channels they can see
/// (Fidaner, Lin and Ozguven's error shift: the red error into green and blue for protanopia and deuteranopia; for
/// tritanopia the blue error onto the red–green axis) and added back. Both steps are linear, so the correction is one
/// 3×3 matrix on linear RGB, <c>M = I + s·E·(I − S)</c> at strength s: the screen filter (ColorBlindFilter) runs it on
/// the 3D frame. The interface instead swaps its key colours for tuned ones (<see cref="ColorVisionPalette"/>), since a
/// matrix on already-saturated UI colours clips them together.
/// </summary>
public static class ColorVision
{
    /// <summary>Setting values, indexed by <see cref="ColorBlindMode"/>.</summary>
    public static readonly string[] ModeNames = { "off", "protanopia", "deuteranopia", "tritanopia" };

    private static readonly float[][] Simulations =
    {
        new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f },
        new[] { 0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f },
        new[] { 0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f },
        new[] { 1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f },
    };

    private static readonly float[] RedGreenShift = { 0f, 0f, 0f, 0.7f, 1f, 0f, 0.7f, 0f, 1f };
    private static readonly float[] BlueYellowShift = { 0f, 0f, 0.7f, 0f, 0f, -0.7f, 0f, 0f, 0f };

    private static readonly float[] ToLinearTable = BuildLinearTable();

    /// <summary>The mode a setting value names; unknown values are <see cref="ColorBlindMode.Off"/>.</summary>
    public static ColorBlindMode Parse(string? name)
    {
        int i = Array.IndexOf(ModeNames, name?.Trim().ToLowerInvariant());
        return i < 0 ? ColorBlindMode.Off : (ColorBlindMode)i;
    }

    public static string Name(ColorBlindMode mode) => ModeNames[(int)mode];

    /// <summary>The matrix that shows linear RGB as a viewer with <paramref name="mode"/> sees it (row-major).</summary>
    public static float[] Simulation(ColorBlindMode mode) => (float[])Simulations[(int)mode].Clone();

    /// <summary>
    /// The daltonising matrix (row-major, linear RGB) for <paramref name="mode"/> at <paramref name="strength"/> 0–1:
    /// the identity when off or at strength 0.
    /// </summary>
    public static float[] Correction(ColorBlindMode mode, float strength)
    {
        var m = new float[9];
        m[0] = m[4] = m[8] = 1f;
        float s = Math.Clamp(strength, 0f, 1f);
        if (mode == ColorBlindMode.Off || s <= 0f) return m;
        var sim = Simulations[(int)mode];
        var shift = mode == ColorBlindMode.Tritanopia ? BlueYellowShift : RedGreenShift;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                float sum = 0f;
                for (int k = 0; k < 3; k++) sum += shift[r * 3 + k] * ((k == c ? 1f : 0f) - sim[k * 3 + c]);
                m[r * 3 + c] += s * sum;
            }
        return m;
    }

    /// <summary>Runs an sRGB colour through a row-major linear-RGB matrix, clamped to the gamut.</summary>
    public static Rgb Transform(Rgb c, float[] m)
    {
        float r = ToLinearTable[c.R], g = ToLinearTable[c.G], b = ToLinearTable[c.B];
        return new Rgb(
            Encode(m[0] * r + m[1] * g + m[2] * b),
            Encode(m[3] * r + m[4] * g + m[5] * b),
            Encode(m[6] * r + m[7] * g + m[8] * b));
    }

    /// <summary>How far apart two colours look to a viewer with <paramref name="mode"/> (CIE76 ΔE of the simulated colours).</summary>
    public static double SeenDistance(Rgb a, Rgb b, ColorBlindMode mode)
    {
        var sim = Simulations[(int)mode];
        var (l1, a1, b1) = Lab(Transform(a, sim));
        var (l2, a2, b2) = Lab(Transform(b, sim));
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    /// <summary>"#rrggbb" (or "rrggbb") to a colour; false when malformed.</summary>
    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = default;
        if (text == null) return false;
        var s = text.StartsWith('#') ? text.AsSpan(1) : text.AsSpan();
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>CIELAB (D65) of an sRGB colour.</summary>
    public static (double L, double A, double B) Lab(Rgb c)
    {
        double r = ToLinearTable[c.R], g = ToLinearTable[c.G], b = ToLinearTable[c.B];
        double x = F((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047);
        double y = F(0.2126 * r + 0.7152 * g + 0.0722 * b);
        double z = F((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
    }

    private static byte Encode(float linear)
    {
        double v = Math.Clamp(linear, 0f, 1f);
        v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        return (byte)Math.Round(v * 255.0);
    }

    private static float[] BuildLinearTable()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            t[i] = (float)(v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4));
        }
        return t;
    }
}
