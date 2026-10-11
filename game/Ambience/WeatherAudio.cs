using Godot;
using Strata.Core.Gameplay.Weather;
using Strata.Game.Audio;

namespace Strata.Game.Ambience;

/// <summary>
/// The weather's sound: light and heavy rain, wind, blizzard and sandstorm loops and thunder, on the Outdoors bus (it
/// feeds the Ambient bus, so the Ambient volume slider covers it). Under a roof or in a cave mouth the weather is
/// heard through that bus's low-pass (<see cref="AudioEnvironment"/>); deep underground it falls silent.
/// </summary>
public sealed partial class WeatherAudio : Node
{
    private const string Bus = AudioEnvironment.Outdoors;

    private AmbienceLoops.Fader _light = null!, _heavy = null!, _wind = null!, _blizzard = null!, _sand = null!;
    private AudioStreamPlayer _thunder = null!;
    private readonly RandomNumberGenerator _rng = new();
    private const float Audible = 0.12f; // a loop at least this loud is captioned
    private float _captionIn;

    public override void _Ready()
    {
        Name = "WeatherAudio";
        AudioEnvironment.Ensure();
        _light = AmbienceLoops.Player(this, "rain_light", Bus);
        _heavy = AmbienceLoops.Player(this, "rain_heavy", Bus);
        _wind = AmbienceLoops.Player(this, "wind", Bus);
        _blizzard = AmbienceLoops.Player(this, "blizzard", Bus);
        _sand = AmbienceLoops.Player(this, "sandstorm", Bus);
        _thunder = new AudioStreamPlayer { Name = "Thunder", Bus = Bus };
        AddChild(_thunder);
        _rng.Randomize();
    }

    /// <param name="cover">How much of the weather reaches the camera (0 deep underground … 1 open or just under a roof).</param>
    /// <param name="outdoor">Smoothed open-sky exposure at the camera (0 enclosed … 1 open).</param>
    public void Drive(LocalWeather local, float strength, float windSpeed, float cover, float outdoor, float dt)
    {
        bool rain = local is LocalWeather.Rain or LocalWeather.Storm;
        bool storm = local is LocalWeather.Storm;
        bool snow = local is LocalWeather.Snow or LocalWeather.Blizzard;
        float heavy = rain ? Mathf.Clamp((strength - 0.45f) / 0.45f, 0f, 1f) + (storm ? 0.25f : 0f) : 0f;
        _light.Drive(rain ? strength * (1f - 0.6f * Mathf.Min(heavy, 1f)) * cover : 0f, dt);
        _heavy.Drive(heavy * strength * cover, dt);
        float gust = Mathf.Clamp((windSpeed - 0.2f) / 0.7f, 0f, 1f);
        _wind.Drive(gust * gust * 0.8f * cover * Mathf.Lerp(0.35f, 1f, outdoor), dt, 0.3f);
        _blizzard.Drive(snow ? strength * (local == LocalWeather.Blizzard ? 1f : 0.35f) * cover : 0f, dt);
        _sand.Drive(local == LocalWeather.Sandstorm ? cover * Mathf.Lerp(0.5f, 1f, gust) : 0f, dt);
        if ((_captionIn -= dt) <= 0f) CaptionLoops();
    }

    /// <summary>Once a second, tells the subtitles (X-12) which weather loops can be heard; their lines stay up meanwhile.</summary>
    private void CaptionLoops()
    {
        _captionIn = 1f;
        if (_light.Level > Audible) Sfx.Report("rain_light");
        if (_heavy.Level > Audible) Sfx.Report("rain_heavy");
        if (_wind.Level > Audible) Sfx.Report("wind");
        if (_blizzard.Level > Audible) Sfx.Report("blizzard");
        if (_sand.Level > Audible) Sfx.Report("sandstorm");
    }

    public void Thunder(bool near, float power)
    {
        string id = near ? "thunder_near" : "thunder_far";
        var stream = AudioDiagnostics.Load($"{AudioDiagnostics.SfxDirectory}/{id}_{_rng.RandiRange(1, 3)}.wav");
        if (stream == null) return;
        _thunder.Stream = stream;
        _thunder.VolumeDb = Mathf.LinearToDb(Mathf.Lerp(0.55f, 1f, power)) + (near ? 0f : -4f);
        _thunder.PitchScale = _rng.RandfRange(0.9f, 1.08f);
        _thunder.Play();
        Sfx.Report(id);
    }
}
