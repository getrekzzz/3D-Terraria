using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Weather;
using Strata.Game.World;

namespace Strata.Game.Ambience;

/// <summary>
/// Draws the weather around the camera: GPU rain streaks with splashes on whatever they land on, snow and blizzards,
/// blowing sand, all inside a box that follows the eye and is cut off by the sky map (<see cref="SkyHeights"/>), so
/// nothing falls in caves or under roofs. Particle counts scale with the weather's strength within the graphics
/// preset's budget (data/config/weather.json). It also wets the terrain (the <c>strata_weather</c> global), feeds the
/// sky and fog (<see cref="EnvironmentController"/>: overcast, wind, haze), throws lightning and plays the loops
/// (<see cref="WeatherAudio"/>).
/// </summary>
public sealed partial class WeatherRenderer : Node3D
{
    private const float SampleSeconds = 0.25f;
    private static readonly StringName WeatherGlobal = "strata_weather";

    private WorldRuntime _runtime = null!;
    private WeatherConfig _config = null!;
    private WeatherBudget _budget = null!;
    private SkyHeights _sky = null!;
    private WeatherAudio _audio = null!;
    private readonly Strata.Core.Conditions.ConditionContext _ctx = new();
    private System _rain = null!, _snow = null!, _sand = null!, _splash = null!;
    private float _sample;
    private LocalWeather _local;
    private float _cover; // how much of the sky the camera sees above it (0 deep underground … 1 open)
    private float _wet, _puddles, _overcast, _haze;

    /// <summary>The weather at the camera's column on the surface (what is falling above it).</summary>
    public LocalWeather Local => _local;

    /// <summary>Particles alive at most right now across the systems (budget × strength), for probes and F3.</summary>
    public int ActiveParticles => _rain.Alive + _snow.Alive + _sand.Alive + _splash.Alive;

    /// <summary>The sky map heights around the camera (shared with the ambient particles).</summary>
    public SkyHeights Heights => _sky;

    /// <summary>The preset's particle budget over all systems.</summary>
    public int BudgetTotal => _budget.Rain + _budget.Snow + _budget.Sand + _budget.Splash;

    /// <summary>Terrain wetness 0–1 (rises in rain, dries slowly after).</summary>
    public float Wetness => _wet;

    public void Initialize(WorldRuntime runtime, Rendering.GraphicsSettings graphics, WeatherConfig config)
    {
        Name = "Weather";
        _runtime = runtime;
        _config = config;
        _sky = new SkyHeights(runtime.Session);
        // Boxes (radius, top above the eye, height; m) are sized so each budget fills the view densely.
        _rain = new System(this, 0, new Vector2(0.012f, 0.6f), new Color(0.7f, 0.76f, 0.85f), 0.42f, 2f, new Vector3(12f, 11f, 17f));
        _snow = new System(this, 1, new Vector2(0.07f, 0.07f), new Color(0.95f, 0.97f, 1f), 0.9f, 9f, new Vector3(9f, 8f, 13f));
        _sand = new System(this, 2, new Vector2(0.02f, 0.4f), new Color(0.86f, 0.72f, 0.48f), 0.5f, 3f, new Vector3(10f, 5f, 9f));
        _splash = new System(this, 3, new Vector2(0.18f, 0.12f), new Color(0.8f, 0.85f, 0.92f), 0.35f, 0.28f, new Vector3(12f, 0f, 1f));
        _audio = new WeatherAudio();
        AddChild(_audio);
        CreateLightning();
        ApplyBudget(graphics);
        graphics.Changed += _ => ApplyBudget(graphics);
    }

    private void ApplyBudget(Rendering.GraphicsSettings graphics)
    {
        _budget = _config.Presentation.BudgetFor(graphics.Name);
        _rain.SetBudget(_budget.Rain);
        _snow.SetBudget(_budget.Snow);
        _sand.SetBudget(_budget.Sand);
        _splash.SetBudget(_budget.Splash);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.weather");
        float dt = (float)delta;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var eye = camera.GlobalPosition;
        var state = _runtime.Session.Weather;
        _sample -= dt;
        if (_sample <= 0f)
        {
            _sample = SampleSeconds;
            Sample(eye);
        }
        _sky.Update(eye);

        // Strength of each kind at the camera: the world's eased intensity, gone where the sky is out of reach.
        float strength = state.Forced != null ? Mathf.Max(state.Current, 0.8f) : state.Current;
        float reach = _cover;
        bool rain = _local is LocalWeather.Rain or LocalWeather.Storm;
        bool snow = _local is LocalWeather.Snow or LocalWeather.Blizzard;
        bool sand = _local == LocalWeather.Sandstorm;
        float storm = _local is LocalWeather.Storm or LocalWeather.Blizzard ? 1f : 0f;
        float sandStrength = sand ? (state.Forced == LocalWeather.Sandstorm ? 1f : Mathf.Clamp(state.WindSpeed * 1.2f, 0.4f, 1f)) : 0f;

        var wind = new Vector3(state.Wind.X, 0f, state.Wind.Y);
        Position = eye;
        _rain.Drive(rain ? strength * reach : 0f, wind * (6f + 6f * storm), 9.5f + 2.5f * storm, _sky, dt);
        _splash.Drive(rain ? strength * reach : 0f, Vector3.Zero, 0f, _sky, dt);
        _snow.Drive(snow ? Mathf.Max(strength, 0.3f) * reach : 0f, wind * (2.5f + 7f * storm), 1.4f + 1.2f * storm, _sky, dt);
        _sand.Drive(sandStrength * reach, wind.Normalized() * (9f + 10f * state.WindSpeed), 0f, _sky, dt);

        // The ground darkens and shines while rain reaches it, puddles gather on flat tops, and both dry slowly.
        float wetTarget = rain ? strength : 0f;
        _wet = Mathf.MoveToward(_wet, wetTarget, dt * (wetTarget > _wet ? 1f / 25f : 1f / 120f));
        _puddles = Mathf.MoveToward(_puddles, rain ? strength * strength : 0f, dt * (rain ? 1f / 90f : 1f / 200f));
        UpdateLightning(storm > 0f && strength > 0.3f, eye, dt);
        RenderingServer.GlobalShaderParameterSet(WeatherGlobal, new Vector4(_wet, _puddles, _flash, snow ? strength : 0f));

        // Sky and fog: rain clouds close over, sandstorms and blizzards thicken the air into a tinted haze.
        float overcastTarget = rain || snow ? Mathf.Clamp(0.45f + strength * 0.55f, 0f, 1f) : state.Current * 0.6f;
        _overcast = Mathf.MoveToward(_overcast, overcastTarget, dt * 0.08f);
        float hazeTarget = (sand ? sandStrength * 0.85f : 0f) + (snow ? strength * storm * 0.75f + strength * 0.15f : 0f) + (rain ? strength * 0.18f : 0f);
        _haze = Mathf.MoveToward(_haze, hazeTarget * reach, dt * 0.15f);
        var env = _runtime.Environment;
        env.Overcast = _overcast;
        env.WeatherWind = new Vector2(state.Wind.X, state.Wind.Y);
        env.Haze = _haze;
        env.HazeColor = sand ? new Color(0.78f, 0.62f, 0.4f) : snow ? new Color(0.86f, 0.9f, 0.96f) : new Color(0.55f, 0.6f, 0.66f);
        env.Flash = _flash;
        _audio.Drive(_local, strength, state.WindSpeed, _cover, env.Outdoor, dt);
    }

    /// <summary>A few times a second: the surface weather over the camera's column and how open the sky is above it.</summary>
    private void Sample(Vector3 eye)
    {
        var session = _runtime.Session;
        int cx = Mathf.FloorToInt(eye.X / Units.CellSize), cz = Mathf.FloorToInt(eye.Z / Units.CellSize);
        int top = session.Sky.SkyTop(cx, cz);
        float surface = session.Generator.Terrain.SurfaceHeight(cx, cz);
        int at = Mathf.Max(top, Mathf.CeilToInt(surface)) + 1;
        _local = session.Weather.LocalAt(session.ContextAt(new Int3(cx, at, cz), _ctx).Biomes, _ctx.Layer);
        // Under a roof or a little way into a cave the weather still shows outside; far below the surface it is gone.
        float below = (top - eye.Y / Units.CellSize) * Units.CellSize;
        _cover = 1f - Mathf.SmoothStep(14f, 30f, below);
    }
}
