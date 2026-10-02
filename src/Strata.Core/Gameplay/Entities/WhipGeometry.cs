using System.Numerics;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// The shape of a whip's lash over its use (P3-14), shared by the hit test and the renderer: a quadratic Bézier from
/// the hand to the tip. Until <see cref="WindUpEnd"/> the lash winds back and up over the shoulder; it then sweeps
/// forward and is fully extended along the aim at <see cref="CrackAt"/> (the crack), and is drawn back in after.
/// </summary>
public static class WhipGeometry
{
    public const float WindUpEnd = 0.35f, CrackAt = 0.6f;

    /// <summary>The part of the use (fraction of the animation) in which the lash can hit.</summary>
    public const float HitStart = 0.4f, HitEnd = 0.75f;

    /// <summary>
    /// Fills <paramref name="into"/> with points along the lash at use fraction <paramref name="t"/> (0–1), from the hand
    /// at <paramref name="origin"/> to the tip; <paramref name="forward"/> and <paramref name="up"/> are unit vectors.
    /// </summary>
    public static void Points(float t, Vector3 origin, Vector3 forward, Vector3 up, float length, Span<Vector3> into)
    {
        var (tip, control) = Shape(Math.Clamp(t, 0f, 1f), origin, forward, up, length);
        int n = into.Length;
        for (int i = 0; i < n; i++)
        {
            float s = n == 1 ? 1f : (float)i / (n - 1);
            float a = 1f - s;
            into[i] = a * a * origin + 2f * a * s * control + s * s * tip;
        }
    }

    private static (Vector3 Tip, Vector3 Control) Shape(float t, Vector3 origin, Vector3 forward, Vector3 up, float length)
    {
        // Behind and above the shoulder while winding up, fully out along the aim at the crack, half drawn in at the end.
        var back = origin - forward * length * 0.35f + up * length * 0.45f;
        var full = origin + forward * length;
        var rest = origin + forward * length * 0.35f - up * length * 0.1f;
        Vector3 tip;
        float slack;
        if (t < WindUpEnd)
        {
            float k = t / WindUpEnd;
            tip = Vector3.Lerp(origin + up * length * 0.15f, back, Ease(k));
            slack = 0.4f;
        }
        else if (t < CrackAt)
        {
            float k = (t - WindUpEnd) / (CrackAt - WindUpEnd);
            tip = Vector3.Lerp(back, full, k * k); // the lash accelerates into the crack
            slack = 0.4f * (1f - k);
        }
        else
        {
            float k = (t - CrackAt) / (1f - CrackAt);
            tip = Vector3.Lerp(full, rest, Ease(k));
            slack = 0.25f * k;
        }
        var mid = (origin + tip) * 0.5f;
        return (tip, mid + up * length * slack);
    }

    private static float Ease(float k) => k * k * (3f - 2f * k);
}
