using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// One straight stretch of a cave-network passage, from floor point <see cref="A"/> to floor point <see cref="B"/>. The
/// cross-section is an arch over a flat floor (the floor curves up only near the walls), and the floor runs in a straight
/// line between the ends, never steeper than <see cref="CaveTuning.MaxSlope"/>, so a player walks it without jumping.
/// Consecutive stretches share their end points; the rounded ends make the joints seamless.
/// </summary>
public sealed class CavePassageFeature : WorldFeature
{
    private readonly float _dx, _dz, _len2;

    public CavePassageFeature(Vector3 a, Vector3 b, float halfWidth, float height)
    {
        A = a;
        B = b;
        HalfWidth = halfWidth;
        Height = height;
        _dx = b.X - a.X;
        _dz = b.Z - a.Z;
        _len2 = _dx * _dx + _dz * _dz;
        float r = halfWidth * (1f + CaveTuning.PassageWarp) + 3f;
        float lo = MathF.Min(a.Y, b.Y) - CaveTuning.PassageFloorRock - 2f;
        float hi = MathF.Max(a.Y, b.Y) + height * (1f + CaveTuning.CeilingWarp) + 2f;
        Bounds = new Aabb3i(
            new Int3((int)MathF.Floor(MathF.Min(a.X, b.X) - r), (int)MathF.Floor(lo), (int)MathF.Floor(MathF.Min(a.Z, b.Z) - r)),
            new Int3((int)MathF.Ceiling(MathF.Max(a.X, b.X) + r) + 1, (int)MathF.Ceiling(hi) + 1, (int)MathF.Ceiling(MathF.Max(a.Z, b.Z) + r) + 1));
    }

    public Vector3 A { get; }
    public Vector3 B { get; }
    public float HalfWidth { get; }

    /// <summary>Height of the arch over the floor on the passage's axis.</summary>
    public float Height { get; }

    /// <summary>Network level of the room the passage serves (the lower one for a descent).</summary>
    public int Level { get; init; }

    /// <summary>Horizontal length.</summary>
    public float Length => MathF.Sqrt(_len2);

    /// <summary>Rise over run of the floor.</summary>
    public float Slope => _len2 > 1e-6f ? MathF.Abs(B.Y - A.Y) / MathF.Sqrt(_len2) : 0f;

    /// <summary>Horizontal distance of a column from the passage's axis; <paramref name="t"/> is where along it the column
    /// lies (0 at A … 1 at B, clamped, so the ends are round).</summary>
    public float Axis(float x, float z, out float t)
    {
        float px = x - A.X, pz = z - A.Z;
        t = _len2 > 1e-6f ? DetMath.Clamp01((px * _dx + pz * _dz) / _len2) : 0f;
        float ex = px - _dx * t, ez = pz - _dz * t;
        return MathF.Sqrt(ex * ex + ez * ez);
    }

    /// <summary>The floor's height at <paramref name="t"/> along the passage.</summary>
    public float FloorAt(float t) => A.Y + (B.Y - A.Y) * t;

    /// <summary>The arch's height over the floor at normalized distance <paramref name="u"/> from the axis (0 beyond the wall).</summary>
    public float CeilingAt(float u) => u >= 1f ? 0f : Height * MathF.Sqrt(1f - u * u);

    /// <summary>How far the floor curves up into the wall at normalized distance <paramref name="u"/> from the axis.</summary>
    public float RiseAt(float u) => u <= 0.7f ? 0f : Height * 0.45f * DetMath.Square((u - 0.7f) / 0.3f);
}
