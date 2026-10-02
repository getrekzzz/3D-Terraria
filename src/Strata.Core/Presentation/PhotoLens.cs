using System;

namespace Strata.Core.Presentation;

/// <summary>
/// Photo mode's lens as pure logic (X-12): the depth-of-field bands round a focus distance, exposure compensation in
/// stops, the colour filters and the screenshot file name. The game writes the bands to the photo camera's
/// CameraAttributesPractical and the filter index to its post-process layer.
/// </summary>
public static class PhotoLens
{
    public const float MinFocus = 0.5f;
    public const float MaxFocus = 120f;
    public const float MaxStops = 3f;

    /// <summary>Godot's dof_blur_amount at a full blur slider (1 is a smear; this keeps the strongest setting usable).</summary>
    public const float MaxBlurAmount = 0.3f;

    /// <summary>Filter names in index order (the post shader's <c>mode</c> uniform).</summary>
    public static readonly string[] Filters = { "none", "warm", "cool", "mono", "sepia" };

    /// <summary>
    /// Blur bands for a focus distance (metres) and blur strength (0 off … 1). Things nearer than
    /// <see cref="DofBands.NearDistance"/> or beyond <see cref="DofBands.FarDistance"/> blur, ramping over the transitions;
    /// the sharp zone widens with the focus distance as a real lens's does.
    /// </summary>
    public static DofBands Dof(float focus, float blur)
    {
        focus = Math.Clamp(focus, MinFocus, MaxFocus);
        blur = Math.Clamp(blur, 0f, 1f);
        float near = focus * 0.7f;
        return new DofBands(blur > 0.001f, near, near * 0.8f, focus * 1.25f + 0.5f, Math.Max(1f, focus), blur * MaxBlurAmount);
    }

    /// <summary>Exposure multiplier for a compensation in stops (clamped to ±<see cref="MaxStops"/>).</summary>
    public static float ExposureMultiplier(float stops) => MathF.Pow(2f, Math.Clamp(stops, -MaxStops, MaxStops));

    /// <summary>Index of a filter name (case-insensitive), or -1.</summary>
    public static int FilterIndex(string name) =>
        Array.FindIndex(Filters, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sortable, collision-safe file name for a picture taken at <paramref name="time"/>.</summary>
    public static string ScreenshotName(DateTime time) => $"{time:yyyyMMdd-HHmmss-fff}.png";
}

/// <summary>Depth-of-field settings in Godot's terms (distances in metres from the camera).</summary>
public readonly record struct DofBands(bool Enabled, float NearDistance, float NearTransition, float FarDistance, float FarTransition, float Amount);
