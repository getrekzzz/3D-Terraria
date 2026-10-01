using Godot;

namespace Strata.Game.Player;

/// <summary>
/// Screen shake from explosions and heavy impacts: callers add trauma (0–1); the camera jitters by its square and
/// it decays over about a second. Off when the player disables camera shake.
/// </summary>
public static class CameraShake
{
    private static float _trauma;
    private static float _time;

    /// <summary>The trauma left now (0–1; the camera.trauma probe).</summary>
    public static float Trauma => _trauma;

    public static void Kick(float amount) => _trauma = Mathf.Min(1f, _trauma + amount);

    /// <summary>The camera offset for this frame (metres).</summary>
    public static Vector2 Sample(double delta)
    {
        _time += (float)delta;
        _trauma = Mathf.Max(0f, _trauma - (float)delta * 1.2f);
        if (_trauma <= 0f || !Settings.UserSettings.Current.CameraShake) return Vector2.Zero;
        float s = _trauma * _trauma * 0.35f;
        return new Vector2(Mathf.Sin(_time * 53f) + Mathf.Sin(_time * 31f) * 0.5f, Mathf.Sin(_time * 47f + 1.3f) + Mathf.Sin(_time * 23f) * 0.5f) * s;
    }
}
