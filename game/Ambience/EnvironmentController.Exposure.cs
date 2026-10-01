using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Ambience;

/// <summary>
/// Eye adaptation on every preset: <see cref="ExposureModel"/> (Core) eases the camera's exposure multiplier toward
/// what the eye wants where the camera is (caveBoost sealed from the sky; in the open the night boost, or by day the
/// biome's exposure bias that keeps sunlit sand and snow from blowing out), opening up slowly and closing down fast.
/// Presets with auto exposure also let Godot meter the frame, but only within the narrow autoMin–autoMax window of
/// data/config/lighting.json; the two multipliers multiply.
/// </summary>
public sealed partial class EnvironmentController
{
    /// <summary>Smallest change of the adapted multiplier pushed to the camera (no per-frame RenderingServer churn).</summary>
    private const float ExposureWriteStep = 0.002f;

    private ExposureSettings _exposureSettings = null!;
    private ExposureModel _exposure = null!;
    private float _autoMinSensitivity, _autoMaxSensitivity;
    private float _biomeExposure = 1f;
    private float _writtenExposure;

    /// <summary>The adapted exposure multiplier (1 in the open forest by day; caveBoost once adapted to a cave).</summary>
    public float Exposure => _exposure.Current;

    /// <summary>The camera attributes with Godot's auto exposure confined to the configured window (enabled per preset).</summary>
    private CameraAttributesPractical CreateExposure(ExposureSettings settings)
    {
        _exposureSettings = settings;
        _exposure = new ExposureModel(settings);
        (_autoMinSensitivity, _autoMaxSensitivity) = ExposureModel.AutoWindow(settings);
        var camera = new CameraAttributesPractical { AutoExposureSpeed = settings.AutoSpeed };
        WriteExposure(camera, _exposure.Current);
        return camera;
    }

    /// <summary>
    /// Steps the eye toward the exposure wanted at <paramref name="outdoor"/> (0 sealed … 1 open sky, unsmoothed: the
    /// model's own rates set how fast it follows) and writes it to the camera once it has moved noticeably.
    /// </summary>
    private void UpdateExposure(float outdoor, float dt)
    {
        _exposure.Step(ExposureModel.Target(_exposureSettings, outdoor, Daylight, _biomeExposure), dt);
        if (Mathf.Abs(_exposure.Current - _writtenExposure) > ExposureWriteStep) WriteExposure(_camera, _exposure.Current);
    }

    /// <summary>
    /// Sets the multiplier. Godot applies it to the lights, sky and emission before its auto exposure meters the frame,
    /// so the metering would undo it (a dimmed desert reads darker and gets brightened back). Scaling the metering key
    /// and its luminance window by the same factor makes Godot meter as if at exposure 1: its correction stays within
    /// autoMin–autoMax of the scene itself and multiplies with the model instead of cancelling it.
    /// </summary>
    private void WriteExposure(CameraAttributesPractical camera, float multiplier)
    {
        _writtenExposure = multiplier;
        camera.ExposureMultiplier = multiplier;
        camera.AutoExposureScale = _exposureSettings.AutoKey * multiplier;
        camera.AutoExposureMinSensitivity = _autoMinSensitivity * multiplier;
        camera.AutoExposureMaxSensitivity = _autoMaxSensitivity * multiplier;
    }

    /// <summary>Eases the biome's exposure bias like the rest of the biome look (<paramref name="k"/> from BlendBiome).</summary>
    private void BlendBiomeExposure(float k) => _biomeExposure = Mathf.Lerp(_biomeExposure, Biome?.ExposureBias ?? 1f, k);
}
