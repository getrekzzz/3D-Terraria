using Godot;

namespace Strata.Game.Ambience;

public sealed partial class WeatherRenderer
{
    private DirectionalLight3D _bolt = null!;
    private readonly RandomNumberGenerator _rng = new();
    private float _nextStrike = 4f;
    private float _strikeAge = -1f;
    private float _strikePower;
    private float _thunderIn = -1f;
    private float _flash;

    /// <summary>Lightning strikes so far (probes, tests).</summary>
    public int Strikes { get; private set; }

    /// <summary>A shadowless cold light from high above, pulsed for each strike.</summary>
    private void CreateLightning()
    {
        _rng.Randomize();
        _bolt = new DirectionalLight3D
        {
            Name = "Lightning",
            ShadowEnabled = false,
            LightColor = new Color(0.78f, 0.84f, 1f),
            LightEnergy = 0f,
            Visible = false,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        AddChild(_bolt);
    }

    /// <summary>Forces a strike now (the <c>lightning</c> command and screenshots).</summary>
    public void Strike() => _nextStrike = 0f;

    /// <summary>
    /// In a storm a strike comes every few seconds: two or three flickers over a fraction of a second light the land
    /// from above (only where the sky is in view), and the thunder follows after a delay that tells the distance.
    /// </summary>
    private void UpdateLightning(bool storm, Vector3 eye, float dt)
    {
        var p = _config.Presentation;
        if (storm || _nextStrike <= 0f)
        {
            _nextStrike -= dt;
            if (_nextStrike <= 0f)
            {
                _nextStrike = _rng.RandfRange(p.LightningSeconds[0], p.LightningSeconds[1]);
                _strikeAge = 0f;
                _strikePower = _rng.RandfRange(0.5f, 1f);
                float distance = _rng.Randf();
                _thunderIn = Mathf.Lerp(p.ThunderDelay[0], p.ThunderDelay[1], distance);
                _thunderNear = distance < 0.35f;
                _bolt.RotationDegrees = new Vector3(-_rng.RandfRange(55f, 80f), _rng.RandfRange(0f, 360f), 0f);
                Strikes++;
            }
        }
        if (_strikeAge >= 0f)
        {
            _strikeAge += dt;
            // Flicker: a bright first stroke, a dimmer return stroke and a faint tail.
            float t = _strikeAge;
            float f = Pulse(t, 0f, 0.07f) + 0.6f * Pulse(t, 0.13f, 0.06f) + 0.3f * Pulse(t, 0.24f, 0.1f);
            _flash = f * _strikePower * _cover;
            if (t > 0.45f) _strikeAge = -1f;
        }
        else
        {
            _flash = 0f;
        }
        _bolt.Visible = _flash > 0.01f;
        _bolt.LightEnergy = _flash * 4.5f;
        if (_thunderIn >= 0f && (_thunderIn -= dt) < 0f) _audio.Thunder(_thunderNear, _strikePower);
    }

    private bool _thunderNear;

    private static float Pulse(float t, float start, float length)
    {
        float x = (t - start) / length;
        return x is < 0f or > 1f ? 0f : Mathf.Sin(x * Mathf.Pi);
    }
}
