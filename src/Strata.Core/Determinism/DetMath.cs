using System.Runtime.CompilerServices;

namespace Strata.Core.Determinism;

/// <summary>
/// Deterministic math helpers safe for world generation (only + - * / floor sqrt abs min max).
/// See docs/CONVENTIONS.md — transcendental functions are banned in generation code.
/// </summary>
public static class DetMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = InverseLerp(edge0, edge1, x);
        return t * t * (3f - 2f * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Remap(float v, float inMin, float inMax, float outMin, float outMax) =>
        Lerp(outMin, outMax, InverseLerp(inMin, inMax, v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FloorToInt(float v) => (int)MathF.Floor(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Square(float v) => v * v;

    /// <summary>Polynomial smooth minimum (k = blend radius).</summary>
    public static float SmoothMin(float a, float b, float k)
    {
        float h = Clamp01(0.5f + 0.5f * (b - a) / k);
        return Lerp(b, a, h) - k * h * (1f - h);
    }

    public static float SmoothMax(float a, float b, float k) => -SmoothMin(-a, -b, k);

    /// <summary>
    /// Ridge shape with a rounded crest: 1 at n = 0 falling to 0 at |n| = 1 like 1 − |n|, but rounded over a width of
    /// about <paramref name="k"/> around the crest (1 − √(n² + k²), normalised), so noise fed through it makes soft
    /// ridgelines instead of knife edges.
    /// </summary>
    public static float SoftRidge(float n, float k)
    {
        float top = MathF.Sqrt(1f + k * k);
        return (top - MathF.Sqrt(n * n + k * k)) / (top - k);
    }

    /// <summary>Distance from point p to segment ab (3D).</summary>
    public static float DistanceToSegment(
        float px, float py, float pz, float ax, float ay, float az, float bx, float by, float bz, out float t)
    {
        float abx = bx - ax, aby = by - ay, abz = bz - az;
        float apx = px - ax, apy = py - ay, apz = pz - az;
        float len2 = abx * abx + aby * aby + abz * abz;
        t = len2 > 1e-6f ? Clamp01((apx * abx + apy * aby + apz * abz) / len2) : 0f;
        float dx = apx - abx * t, dy = apy - aby * t, dz = apz - abz * t;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
