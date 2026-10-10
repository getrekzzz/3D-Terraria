namespace Strata.Core.Presentation;

/// <summary>
/// Eye adaptation as pure logic. <see cref="Target"/> is the exposure multiplier the eye wants where the camera is:
/// <see cref="ExposureSettings.CaveBoost"/> sealed from the sky, and in the open the night boost by night or the biome's
/// bias by day (bright sand and snow below 1, so they stop blowing out). <see cref="Step"/> eases toward it, opening up
/// slowly and closing down fast, so leaving a cave never flashbangs. The game feeds it the camera's sky exposure
/// (outdoor 0–1), the daylight (0–1) and the blended biome bias each frame and writes <see cref="Current"/> to the
/// camera's exposure multiplier.
/// </summary>
public sealed class ExposureModel
{
    /// <summary>
    /// Luminance per ISO of Godot's auto exposure sensitivity at the default exposure_sensitivity of 100: Godot passes
    /// <c>auto_exposure_min/max_sensitivity * ((12.5 / 100.0) / exposure_sensitivity)</c> to the renderer as luminance
    /// (CameraAttributesPractical::_update_auto_exposure in scene/resources/camera_attributes.cpp), and the metering
    /// multiplies by <c>auto_exposure_scale / clamp(average luminance, min, max)</c>. So the multiplier ranges over
    /// scale / (ISO * 0.00125): the old 85/170 ISO window at scale 0.35 pinned it to 1.65–3.3x and could never darken.
    /// </summary>
    public const float LuminancePerIso = 12.5f / 100f / 100f;

    private readonly ExposureSettings _settings;

    public ExposureModel(ExposureSettings settings) => _settings = settings;

    /// <summary>The adapted multiplier (starts at 1); set it to snap, e.g. after a teleport.</summary>
    public float Current { get; set; } = 1f;

    /// <summary>
    /// The multiplier the eye adapts to: Lerp(caveBoost, Lerp(nightBoost, 1, daylight) * Lerp(1, biomeBias, daylight),
    /// outdoor). Outdoor and daylight are clamped to 0–1.
    /// </summary>
    public static float Target(ExposureSettings s, float outdoor, float daylight, float biomeBias)
    {
        outdoor = Math.Clamp(outdoor, 0f, 1f);
        daylight = Math.Clamp(daylight, 0f, 1f);
        float open = Lerp(s.NightBoost, 1f, daylight) * Lerp(1f, biomeBias, daylight);
        return Lerp(s.CaveBoost, open, outdoor);
    }

    /// <summary>
    /// Eases <see cref="Current"/> toward <paramref name="target"/> over <paramref name="dt"/> seconds: exponential at
    /// <see cref="ExposureSettings.BrightenPerSecond"/> while rising and <see cref="ExposureSettings.DarkenPerSecond"/>
    /// while falling. Exact for any step length, so it never overshoots.
    /// </summary>
    public void Step(float target, float dt)
    {
        if (dt <= 0f) return;
        float rate = target > Current ? _settings.BrightenPerSecond : _settings.DarkenPerSecond;
        Current = target + (Current - target) * MathF.Exp(-rate * dt);
    }

    /// <summary>
    /// Godot auto exposure sensitivity window (ISO) that keeps its multiplier within
    /// [<see cref="ExposureSettings.AutoMin"/>, <see cref="ExposureSettings.AutoMax"/>] at scale
    /// <see cref="ExposureSettings.AutoKey"/>: the lowest sensitivity gives the highest multiplier.
    /// </summary>
    public static (float MinSensitivity, float MaxSensitivity) AutoWindow(ExposureSettings s) =>
        (s.AutoKey / s.AutoMax / LuminancePerIso, s.AutoKey / s.AutoMin / LuminancePerIso);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
