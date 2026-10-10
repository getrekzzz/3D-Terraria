namespace Strata.Core.Presentation;

/// <summary>
/// The UI scale guard: the player's UI scale setting, lowered when the window is too small to hold the interface's
/// layout at that scale, so the logical viewport (window size / scale) never drops below
/// <see cref="MinLogicalWidth"/> × <see cref="MinLogicalHeight"/> and raising the scale cannot push panels off screen.
/// </summary>
public static class UiScale
{
    public const int MinLogicalWidth = 1024;
    public const int MinLogicalHeight = 600;
    public const float Min = 0.5f;
    public const float Max = 3f;

    /// <summary>
    /// The scale in effect for <paramref name="setting"/> in a window of the given pixel size (0 × 0 when unknown, e.g.
    /// headless runs, applies the setting as is). Always within <see cref="Min"/>..<see cref="Max"/>.
    /// </summary>
    public static float Effective(float setting, int windowWidth, int windowHeight)
    {
        float scale = setting;
        if (windowWidth > 0 && windowHeight > 0)
            scale = MathF.Min(scale, MathF.Min((float)windowWidth / MinLogicalWidth, (float)windowHeight / MinLogicalHeight));
        return Math.Clamp(scale, Min, Max);
    }
}
