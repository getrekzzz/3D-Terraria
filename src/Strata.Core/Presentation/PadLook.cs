using System.Numerics;

namespace Strata.Core.Presentation;

/// <summary>
/// Turning the camera with a stick: a radial dead zone (rest drift is ignored whatever its direction), an outer zone
/// (a worn stick that never reaches the rim still gives full speed), and a response curve so small tilts aim finely
/// and full tilt turns fast. The direction is kept; only the length is shaped.
/// </summary>
public static class PadLook
{
    /// <summary>
    /// The stick vector after the zones and curve: zero inside <paramref name="deadzone"/>, length 1 from
    /// <paramref name="outer"/> out, and in between the rescaled length to the power <paramref name="exponent"/>.
    /// </summary>
    public static Vector2 Shape(Vector2 raw, float deadzone, float outer, float exponent)
    {
        float length = raw.Length();
        if (length <= deadzone || length < 1e-6f) return Vector2.Zero;
        float span = MathF.Max(1e-3f, outer - deadzone);
        float t = Math.Clamp((length - deadzone) / span, 0f, 1f);
        return raw / length * MathF.Pow(t, MathF.Max(0.1f, exponent));
    }

    /// <summary>Yaw and pitch speeds (radians per second) for a shaped stick at <paramref name="sensitivity"/>.</summary>
    public static Vector2 Rates(Vector2 shaped, PadConfig pad, float sensitivity)
    {
        float yaw = pad.LookSpeed * MathF.PI / 180f * sensitivity;
        return new Vector2(shaped.X * yaw, shaped.Y * yaw * pad.PitchRatio);
    }
}
