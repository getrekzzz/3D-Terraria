using Godot;
using Strata.Core.Presentation;
using Strata.Core.Simulation;

namespace Strata.Game.Ambience;

public sealed partial class EnvironmentController
{
    private float _time;
    private Vector2 _wind;
    private float _gusts;
    private float _outdoor = 1f;
    private float _eventBlend;
    private Color _biomeFog = new(0.62f, 0.72f, 0.82f), _biomeSky = Colors.White, _biomeAmbient = Colors.White;
    private float _biomeDensity = 1f;
    private FogSettings _fogSettings = null!; // lighting.json fog
    private Vector3 _skyIrradiance = new(0.3f, 0.4f, 0.6f);
    private int _irradianceFrame;

    /// <summary>
    /// Updates the sky for the clock and the camera: <paramref name="depthBelowSurface"/> (m) and
    /// <paramref name="cameraSkyExposure"/> (0 sealed … 1 open sky, the baked voxel value) move fog and haze from
    /// the open-air look to the dark, close gloom of caves, and the eye's exposure with them.
    /// </summary>
    public void Apply(GameClock clock, float depthBelowSurface, float cameraSkyExposure, double delta)
    {
        float dt = (float)delta;
        _time += dt;
        var (sunDir, moonDir) = Celestial(clock);
        Orient(_sun, sunDir);
        Orient(_moon, moonDir);
        Daylight = Mathf.SmoothStep(-0.05f, 0.25f, sunDir.Y);

        // Direct light: what survives the air (white at noon, orange and dim at dusk).
        var sunT = Atmosphere.Transmittance(sunDir);
        float sunPeak = Mathf.Max(sunT.X, Mathf.Max(sunT.Y, sunT.Z));
        _sun.Visible = sunDir.Y > -0.02f && sunPeak > 0.002f;
        _sun.LightColor = sunPeak > 0f ? new Color(sunT.X / sunPeak, sunT.Y / sunPeak, sunT.Z / sunPeak) : Colors.Black;
        _sun.LightEnergy = SunEnergy * sunPeak * Mathf.SmoothStep(-0.02f, 0.04f, sunDir.Y);
        float phaseLight = Mathf.Abs(clock.MoonPhase - 4) / 4f; // phase 0 is the full moon (1), phase 4 the new moon (0)
        var moonT = Atmosphere.Transmittance(moonDir);
        float moonPeak = Mathf.Max(moonT.X, Mathf.Max(moonT.Y, moonT.Z));
        _moon.Visible = moonDir.Y > 0f && Daylight < 0.5f;
        if (_moon.ShadowEnabled == _sun.Visible) _moon.ShadowEnabled = !_sun.Visible; // one shadow-casting sky light at a time (dusk and dawn overlap)
        _moon.LightColor = new Color(0.66f, 0.76f, 1f);
        _moon.LightEnergy = MoonEnergy * Mathf.Lerp(0.2f, 1f, phaseLight) * moonPeak * (1f - Daylight);

        _eventBlend = Mathf.MoveToward(_eventBlend, EventTint.HasValue ? 1f : 0f, dt * 0.3f);
        var eventTint = Colors.White.Lerp(EventTint ?? Colors.White, _eventBlend);
        BlendBiome(delta);
        UpdateClouds(clock, dt);

        if (_irradianceFrame++ % 10 == 0) _skyIrradiance = Atmosphere.Irradiance(sunDir, SkyIntensity) + Atmosphere.Irradiance(moonDir, MoonSkyIntensity(phaseLight));
        var cam = GetViewport()?.GetCamera3D();
        RenderingServer.GlobalShaderParameterSet("strata_camera", cam?.GlobalPosition ?? Vector3.Zero);
        // Every sky material change re-renders the radiance cubemap (ambient light and reflections): refresh it a few
        // times a second, which the slow sun and moon never outpace, instead of every frame.
        _skyRefresh -= dt;
        if (_skyRefresh <= 0f)
        {
            _skyRefresh = SkyRefreshSeconds;
            _sky.SetShaderParameter("sun_dir", sunDir);
            _sky.SetShaderParameter("moon_dir", moonDir);
            _sky.SetShaderParameter("moon_phase", clock.MoonPhase / 8f);
            _sky.SetShaderParameter("sun_intensity", SkyIntensity);
            _sky.SetShaderParameter("moon_intensity", MoonSkyIntensity(phaseLight));
            _sky.SetShaderParameter("sun_light", sunT * 1.6f);
            _sky.SetShaderParameter("moon_light", new Vector3(0.05f, 0.06f, 0.08f) * Mathf.Lerp(0.3f, 1f, phaseLight));
            _sky.SetShaderParameter("sky_ambient", _skyIrradiance * 1.4f + new Vector3(0.004f, 0.006f, 0.012f));
            _sky.SetShaderParameter("night", 1f - Daylight);
            _sky.SetShaderParameter("star_rotation", clock.Hour / 24f * Mathf.Tau);
            _sky.SetShaderParameter("tint", Colors.White.Lerp(eventTint, 0.7f) * Colors.White.Lerp(_biomeSky, 0.6f));
            _sky.SetShaderParameter("ambient_tint", Colors.White.Lerp(_biomeAmbient, 0.5f) * Colors.White.Lerp(eventTint, 0.45f));
        }

        // Rich colour by day (the reference game's saturated palette over realistic materials); it fades somewhat at
        // night (rod vision), keeping moonlit scenes blue rather than grey.
        _env.AdjustmentSaturation = Mathf.Lerp(0.85f, 1.22f, Daylight);
        _env.AdjustmentBrightness = Mathf.Clamp(Settings.UserSettings.Current.Brightness, 0.5f, 2f);
        // Night keeps a floor under the moonlit sky so shapes and creatures stay readable.
        _env.AmbientLightColor = new Color(0.08f, 0.1f, 0.17f);
        _env.AmbientLightSkyContribution = Mathf.Lerp(0.6f, 1f, Daylight);

        var ambient = new Color(_skyIrradiance.X, _skyIrradiance.Y, _skyIrradiance.Z) + new Color(0.045f, 0.06f, 0.1f);
        ambient *= Colors.White.Lerp(eventTint, 0.45f) * Colors.White.Lerp(_biomeAmbient, 0.5f);
        RenderingServer.GlobalShaderParameterSet("strata_sky_ambient", ambient);
        RenderingServer.GlobalShaderParameterSet("strata_daylight", Daylight);
        RenderingServer.GlobalShaderParameterSet("strata_time", _time);
        RenderingServer.GlobalShaderParameterSet("strata_light_dir", sunDir.Y > -0.02f ? sunDir : moonDir);

        float outdoor = Mathf.Clamp(cameraSkyExposure * 1.25f, 0f, 1f);
        UpdateFog(depthBelowSurface, outdoor, eventTint, dt);
        UpdateExposure(outdoor, dt);
    }

    private const float SkyRefreshSeconds = 0.25f;
    private float _skyRefresh;

    /// <summary>Moon radiance scale of the atmosphere: a moonlit night sky, stronger at full moon.</summary>
    private static float MoonSkyIntensity(float phaseLight) => SkyIntensity * 0.02f * Mathf.Lerp(0.25f, 1f, phaseLight);

    /// <summary>
    /// Sun and moon directions: the sun climbs from the eastern horizon at dawn to the western one at dusk and passes
    /// under the world at night, the moon opposite it. Twilight comes from the atmosphere as the sun dips below.
    /// </summary>
    private static (Vector3 Sun, Vector3 Moon) Celestial(GameClock clock)
    {
        float angle = clock.IsDay ? clock.PhaseProgress * Mathf.Pi : Mathf.Pi + clock.PhaseProgress * Mathf.Pi;
        return (Arc(angle), Arc(angle + Mathf.Pi));
    }

    /// <summary>Drifting weather: cloud cover wanders over hours of game time; wind scrolls the layer.</summary>
    private void UpdateClouds(GameClock clock, float dt)
    {
        float hours = clock.Tick / (float)GameClock.TicksPerHour;
        float weather = SmoothNoise(hours / 7f + _seed % 97);
        float cover = CloudCoverOverride ?? Mathf.Lerp(0.18f, 0.78f, weather);
        _wind += new Vector2(7f, 2.6f) * dt;
        RenderingServer.GlobalShaderParameterSet("strata_clouds", new Vector4(_wind.X, _wind.Y, cover, CloudAltitude));
        // Ground wind for grass and foliage: stronger under heavy cover; gusts scroll downwind.
        var dir = new Vector2(7f, 2.6f).Normalized();
        float strength = Mathf.Lerp(0.25f, 0.85f, Mathf.SmoothStep(0.2f, 0.8f, cover));
        _gusts += dt * (3f + 6f * strength);
        RenderingServer.GlobalShaderParameterSet("strata_wind", new Vector4(dir.X, dir.Y, strength, _gusts));
    }

    private void UpdateFog(float depthBelowSurface, float outdoor, Color eventTint, float dt)
    {
        // The gloom closes in gently when the sky is lost and clears fast under it again (leaving a cave).
        float rate = outdoor > _outdoor ? _fogSettings.ClearPerSecond : _fogSettings.ClosePerSecond;
        _outdoor = Mathf.Lerp(_outdoor, outdoor, 1f - Mathf.Exp(-dt * rate));
        float under = Mathf.Clamp(depthBelowSurface / 30f, 0f, 1f);
        float deep = Mathf.Max(under, (1f - _outdoor) * 0.8f);
        // Open air: clear near the camera, melting into the sky colour at the edge of the streamed world.
        // Underground: close, dark gloom tinted by the biome (crypt murk, cinder haze).
        var skyFog = _biomeFog.Lerp(new Color(0.03f, 0.04f, 0.07f), 1f - Daylight);
        var deepFog = new Color(0.015f, 0.016f, 0.022f).Lerp(_biomeFog * 0.2f, 0.5f);
        _env.FogLightColor = skyFog.Lerp(deepFog, deep) * Colors.White.Lerp(eventTint, 0.5f);
        _env.FogAerialPerspective = Mathf.Lerp(0.8f, 0f, deep);
        float reach = ViewDistance / Mathf.Max(_biomeDensity, 0.25f);
        // The haze starts within the near cap even when the horizon pushes the fog's end far out, so distant sunlit
        // land (sand, snow) fades into the sky instead of glaring at full brightness.
        _env.FogDepthBegin = Mathf.Lerp(Mathf.Min(reach * 0.3f, _fogSettings.NearCap / Mathf.Max(_biomeDensity, 0.5f)), 2f, deep);
        _env.FogDepthEnd = Mathf.Lerp(ViewDistance * 1.02f, 42f / Mathf.Max(_biomeDensity, 0.5f), deep);
        _env.FogDensity = Mathf.Lerp(1f, 0.92f, deep);
        _env.FogSunScatter = 0.08f * (1f - deep);
        // Deep in a cave the sky background only shows past the streamed chunks (the far side of the underworld, a
        // cavern's unloaded edge): the gloom covers it there, or the cave-adapted eye would see a white sky. A shaft or
        // chasm open to the sky keeps its sky.
        _env.FogSkyAffect = under * (1f - _outdoor);

        // Volumetric haze and light shafts outdoors; in caves only carried lights glow in a faint mist.
        _env.VolumetricFogDensity = Mathf.Lerp(0.006f, 0.004f, deep) * Mathf.Clamp(_biomeDensity, 0.5f, 3f);
        _env.VolumetricFogLength = Mathf.Min(ViewDistance, 96f);
        _env.VolumetricFogAmbientInject = 0.35f * (1f - deep);
        _env.VolumetricFogSkyAffect = 0f;
        _sun.LightVolumetricFogEnergy = 1.4f * (1f - deep);
        _moon.LightVolumetricFogEnergy = 1.4f * (1f - deep);
    }

    /// <summary>Eases the biome look towards the current biome's (a few seconds to cross into a new one).</summary>
    private void BlendBiome(double delta)
    {
        float k = 1f - Mathf.Exp(-(float)delta * 0.8f);
        var b = Biome;
        _biomeFog = _biomeFog.Lerp(b != null ? Color.FromHtml(b.FogColor) : new Color(0.62f, 0.72f, 0.82f), k);
        _biomeSky = _biomeSky.Lerp(b != null ? Color.FromHtml(b.SkyTint) : Colors.White, k);
        _biomeAmbient = _biomeAmbient.Lerp(b != null ? Color.FromHtml(b.AmbientTint) : Colors.White, k);
        _biomeDensity = Mathf.Lerp(_biomeDensity, b?.FogDensity ?? 1f, k);
        BlendBiomeExposure(k);
    }

    /// <summary>Point on the celestial circle: angle 0 east horizon, π/2 high in the south, π west horizon.</summary>
    private static Vector3 Arc(float angle) => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.35f).Normalized();

    private static void Orient(DirectionalLight3D light, Vector3 towardLight)
    {
        var up = Mathf.Abs(towardLight.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        light.LookAtFromPosition(Vector3.Zero, -towardLight, up);
    }

    /// <summary>Smooth 1D value noise in 0..1.</summary>
    private static float SmoothNoise(float x)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i;
        f = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i), Hash(i + 1), f);
    }

    private static float Hash(int n)
    {
        uint h = (uint)n * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x85EBCA77u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }
}
