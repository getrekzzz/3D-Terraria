using Godot;

namespace Strata.Game.Ambience;

/// <summary>
/// What the weather does to the sky (set every frame by <see cref="WeatherRenderer"/>): rain clouds close over and
/// dim the sun, the wind moves clouds, grass and trees, sandstorms and blizzards thicken the air into a tinted haze,
/// and lightning flashes brighten everything for an instant.
/// </summary>
public sealed partial class EnvironmentController
{
    /// <summary>Rain cloud cover 0–1 (raises the drifting cover and dims the sun).</summary>
    public float Overcast { get; set; }

    /// <summary>The weather's wind on the ground plane (x, z), speed 0–1.</summary>
    public Vector2 WeatherWind { get; set; } = new(0.15f, 0.05f);

    /// <summary>Airborne sand or snow thickness at the camera 0–1, in <see cref="HazeColor"/>.</summary>
    public float Haze { get; set; }

    public Color HazeColor { get; set; } = new(0.6f, 0.62f, 0.66f);

    /// <summary>Lightning flash 0–1.</summary>
    public float Flash { get; set; }

    /// <summary>Metres the camera is below the local terrain surface (negative above it).</summary>
    public float Depth { get; private set; }

    /// <summary>Smoothed open-sky exposure at the camera (0 sealed … 1 open sky).</summary>
    public float Outdoor => _outdoor;

    /// <summary>Wind direction and strength for the grass, foliage and flame shaders: a breeze on calm days, a gale in storms.</summary>
    private (Vector2 Dir, float Strength, float Scroll) GroundWind()
    {
        var w = WeatherWind;
        float speed = w.Length();
        var dir = speed > 1e-4f ? w / speed : new Vector2(0.94f, 0.35f);
        return (dir, Mathf.Lerp(0.2f, 1.7f, Mathf.SmoothStep(0f, 1f, speed)), 3f + 14f * speed);
    }

    /// <summary>The sky cover with rain clouds rolled in.</summary>
    private float WeatherCover(float drifting) => Mathf.Lerp(drifting, 0.97f, Overcast);

    /// <summary>Direct light left under the clouds (1 clear … about a third in a downpour).</summary>
    private float SunThroughClouds => 1f - 0.65f * Overcast;

    /// <summary>Greys the open-air fog under rain clouds and tints it with sand or snow in a sandstorm or blizzard.</summary>
    private void ApplyWeatherFogColor(ref Color skyFog)
    {
        var grey = new Color(0.42f, 0.46f, 0.52f) * Mathf.Lerp(0.15f, 1f, Daylight);
        skyFog = skyFog.Lerp(grey, Overcast * 0.6f);
        var haze = HazeColor * Mathf.Lerp(0.12f, 1f, Daylight);
        skyFog = skyFog.Lerp(haze, Mathf.Clamp(Haze * 1.2f, 0f, 1f));
    }

    /// <summary>Pulls the open-air fog in: a little under rain, to a few metres in a thick sandstorm or blizzard.</summary>
    private void ApplyWeatherFogReach(float deep)
    {
        float open = 1f - deep;
        float thick = Mathf.Clamp(Haze, 0f, 1f) * open;
        _env.FogDepthBegin = Mathf.Lerp(_env.FogDepthBegin, 0.5f, thick);
        _env.FogAerialPerspective *= 1f - Mathf.Max(thick, Overcast * 0.5f * open); // the haze's own colour, not the sky's
        _env.FogDepthEnd = Mathf.Lerp(_env.FogDepthEnd, 14f, thick) * Mathf.Lerp(1f, 0.6f, Overcast * open);
    }
}
