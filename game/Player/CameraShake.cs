using Godot;

namespace Strata.Game.Player;

/// <summary>
/// Screen shake from explosions and heavy impacts: callers add trauma (0–1); the camera jitters by its square and
/// it decays over about a second. A punch is a single sharp jolt instead (a boss waking or dying, a heavy hit or
/// landing): the view dips and rolls, then settles. Both are off when the player disables camera shake.
/// </summary>
public static class CameraShake
{
    private static float _trauma;
    private static float _time;
    private static float _punch, _punchTime, _punchLength = 1f, _punchRoll;
    private static int _punchSide = 1;

    /// <summary>The camera roll for this frame (radians), from punches.</summary>
    public static float Roll { get; private set; }

    /// <summary>Punches so far (probe <c>camera.punches</c>).</summary>
    public static int Punches { get; private set; }

    public static void Kick(float amount) => _trauma = Mathf.Min(1f, _trauma + amount);

    /// <summary>A jolt of <paramref name="strength"/> (0–1) that settles over <paramref name="seconds"/>, rolling up to <paramref name="rollDeg"/>.</summary>
    public static void Punch(float strength, float seconds = 0.25f, float rollDeg = 1.5f)
    {
        strength = Mathf.Clamp(strength, 0f, 1f);
        // A weaker punch never cuts a stronger one short.
        float remaining = _punch * Envelope();
        if (strength < remaining) return;
        _punch = strength;
        _punchTime = 0f;
        _punchLength = Mathf.Max(0.05f, seconds);
        _punchSide = -_punchSide; // alternate the roll so repeated hits do not lean one way
        _punchRoll = Mathf.DegToRad(rollDeg) * _punchSide;
        Punches++;
    }

    private static float Envelope()
    {
        float t = Mathf.Clamp(_punchTime / _punchLength, 0f, 1f);
        return (1f - t) * (1f - t);
    }

    /// <summary>The camera offset for this frame (metres); also updates <see cref="Roll"/>.</summary>
    public static Vector2 Sample(double delta)
    {
        _time += (float)delta;
        _trauma = Mathf.Max(0f, _trauma - (float)delta * 1.2f);
        _punchTime += (float)delta;
        if (_punchTime >= _punchLength) _punch = 0f;
        Roll = 0f;
        if (!Settings.UserSettings.Current.CameraShake) return Vector2.Zero;
        var offset = Vector2.Zero;
        if (_punch > 0f)
        {
            // Dip fast then spring back once: the envelope times a half-period bounce.
            float e = Envelope() * _punch;
            float bounce = Mathf.Cos(_punchTime / _punchLength * Mathf.Pi * 1.5f);
            offset.Y = -0.22f * e * bounce;
            Roll = _punchRoll * e * 2f * bounce;
        }
        if (_trauma <= 0f) return offset;
        float s = _trauma * _trauma * 0.35f;
        return offset + new Vector2(Mathf.Sin(_time * 53f) + Mathf.Sin(_time * 31f) * 0.5f, Mathf.Sin(_time * 47f + 1.3f) + Mathf.Sin(_time * 23f) * 0.5f) * s;
    }
}
