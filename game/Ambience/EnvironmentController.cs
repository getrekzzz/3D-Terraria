using Godot;
using Strata.Core.Simulation;

namespace Strata.Game.Ambience;

/// <summary>
/// Sky, sun/moon, ambient globals and fog driven by the world clock and the camera's depth. Environment
/// ambient light is black: ambient comes from baked voxel sky light through the shared shaders.
/// </summary>
public sealed partial class EnvironmentController : Node3D
{
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _env = null!;
    private ShaderMaterial _sky = null!;
    private float _time;

    public float Daylight { get; private set; } = 1f;

    public override void _Ready()
    {
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.Black,
            AmbientLightEnergy = 0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.1f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogDensity = 0.0025f,
            FogSkyAffect = 0.25f,
            SsaoEnabled = true,
            SsaoRadius = 0.8f,
            SsaoIntensity = 1.6f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightColor = new Color(1f, 0.96f, 0.88f),
            LightEnergy = 1.6f,
            DirectionalShadowMaxDistance = 80f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        _moon = new DirectionalLight3D
        {
            Name = "Moon",
            ShadowEnabled = true,
            LightColor = new Color(0.55f, 0.65f, 1f),
            LightEnergy = 0.18f,
            DirectionalShadowMaxDistance = 60f,
        };
        AddChild(_sun);
        AddChild(_moon);
    }

    /// <summary>Tint of the active world event (crimson moon…), or null.</summary>
    public Color? EventTint { get; set; }

    private float _eventBlend;

    /// <summary>Updates lighting for the given clock; <paramref name="depthBelowSurface"/> (m) darkens fog underground.</summary>
    public void Apply(GameClock clock, float depthBelowSurface, double delta)
    {
        _time += (float)delta;
        float hour = clock.Hour;
        float dayT = Mathf.Clamp((hour - GameClock.DawnHour) / (GameClock.DuskHour - GameClock.DawnHour), 0f, 1f);
        float nightHours = 24f - (GameClock.DuskHour - GameClock.DawnHour);
        float nightT = clock.IsDay ? 0f : ((hour >= GameClock.DuskHour ? hour - GameClock.DuskHour : hour + 24f - GameClock.DuskHour) / nightHours);

        var sunDir = Arc(dayT, clock.IsDay ? 1f : -1f);
        var moonDir = Arc(nightT, clock.IsDay ? -1f : 1f);
        Orient(_sun, sunDir);
        Orient(_moon, moonDir);

        float elevation = Mathf.Max(sunDir.Y, 0f);
        Daylight = Mathf.SmoothStep(-0.05f, 0.25f, sunDir.Y);
        float dusk = clock.IsDay ? Mathf.Clamp(1f - Mathf.Abs(dayT - 0.5f) * 2f, 0f, 1f) : 0f;
        dusk = Mathf.Pow(1f - dusk, 6f) * Daylight;

        _sun.Visible = sunDir.Y > -0.05f;
        _sun.LightEnergy = 1.7f * Mathf.SmoothStep(0f, 0.2f, elevation);
        _sun.LightColor = new Color(1f, 0.93f, 0.82f).Lerp(new Color(1f, 0.62f, 0.35f), dusk);
        _moon.Visible = !clock.IsDay && moonDir.Y > 0f;
        _moon.LightEnergy = 0.2f * (1f - Mathf.Abs(clock.MoonPhase - 4) / 4f * 0.7f);

        var dayAmbient = new Color(0.55f, 0.62f, 0.72f);
        var nightAmbient = new Color(0.07f, 0.09f, 0.16f);
        var ambient = nightAmbient.Lerp(dayAmbient, Daylight).Lerp(new Color(0.75f, 0.5f, 0.4f), dusk * 0.4f);
        _eventBlend = Mathf.MoveToward(_eventBlend, EventTint.HasValue ? 1f : 0f, (float)delta * 0.3f);
        var eventTint = Colors.White.Lerp(EventTint ?? Colors.White, _eventBlend);
        ambient *= Colors.White.Lerp(eventTint, 0.45f);
        RenderingServer.GlobalShaderParameterSet("strata_sky_ambient", ambient);
        RenderingServer.GlobalShaderParameterSet("strata_daylight", Daylight);
        RenderingServer.GlobalShaderParameterSet("strata_time", _time);

        _sky.SetShaderParameter("daylight", Daylight);
        _sky.SetShaderParameter("dusk", dusk);
        _sky.SetShaderParameter("sun_dir", sunDir);
        _sky.SetShaderParameter("moon_dir", moonDir);
        _sky.SetShaderParameter("moon_phase", clock.MoonPhase / 8f);
        _sky.SetShaderParameter("tint", Colors.White.Lerp(eventTint, 0.7f));

        // Fog: hazy outdoors, thick and dark in the depths.
        float under = Mathf.Clamp(depthBelowSurface / 30f, 0f, 1f);
        var skyFog = new Color(0.62f, 0.72f, 0.82f).Lerp(new Color(0.03f, 0.04f, 0.07f), 1f - Daylight);
        _env.FogLightColor = skyFog.Lerp(new Color(0.02f, 0.02f, 0.03f), under) * Colors.White.Lerp(eventTint, 0.5f);
        _env.FogDensity = Mathf.Lerp(0.0022f, 0.018f, under);
        _env.FogSkyAffect = Mathf.Lerp(0.25f, 1f, under);
    }

    /// <summary>Arc across the sky from east (t=0) through zenith to west (t=1); sign flips it below the horizon.</summary>
    private static Vector3 Arc(float t, float sign)
    {
        float a = t * Mathf.Pi;
        return new Vector3(Mathf.Cos(a), Mathf.Sin(a) * sign, 0.35f).Normalized();
    }

    private static void Orient(DirectionalLight3D light, Vector3 towardLight)
    {
        var up = Mathf.Abs(towardLight.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        light.LookAtFromPosition(Vector3.Zero, -towardLight, up);
    }
}
