using System.Numerics;
using Strata.Core.Common;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Where a melee blade is in the world: the plane a swing sweeps (Terraria's overhead arc, stood up in 3D) and the line
/// a thrust stabs along. Forward F is the facing yaw, U world up tilted by <see cref="PlaneTilt"/> toward the right R
/// (F×U); the blade at angle θ (a <see cref="SwingCurve.Blade"/> value) points along cos θ·F + sin θ·U from a pivot at the
/// right shoulder. The melee system tests hits against it and the engine draws the trail and the first-person arc from
/// it, so what is seen is what hits. Pure and deterministic.
/// </summary>
public readonly struct SwingPlane
{
    /// <summary>How far (degrees) the swing plane leans toward the right, so the arc comes over the right shoulder.</summary>
    public const float PlaneTilt = 15f;

    /// <summary>Half the blade's thickness (m) for hit tests.</summary>
    public const float BladeHalfWidth = 0.15f;

    /// <summary>How far (m) off the swing plane a creature still gets hit: forgiveness for aiming a flat arc in 3D.</summary>
    public const float LateralTolerance = 0.5f;

    /// <summary>Pivot (shoulder) height above the body's centre, and its offset toward the right (m).</summary>
    public const float PivotUp = 0.45f, PivotRight = 0.2f;

    /// <summary>Reach (m) past the blade's own length: the arm.</summary>
    public const float SwingArm = 0.6f, ThrustArm = 0.4f;

    /// <summary>The swept angle of one hit-test step (degrees): a fast swing is still tested along its whole arc.</summary>
    public const float StepDeg = 15f;

    public SwingPlane(Vector3 center, float yaw, float reach, float arc = 150f)
    {
        Forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        Right = Vector3.Cross(Forward, Vector3.UnitY);
        float tilt = PlaneTilt * MathF.PI / 180f;
        Up = Vector3.UnitY * MathF.Cos(tilt) + Right * MathF.Sin(tilt);
        Pivot = center + new Vector3(0, PivotUp, 0) + Right * PivotRight;
        Reach = reach;
        ArcScale = arc / 150f;
    }

    public Vector3 Forward { get; }
    public Vector3 Up { get; }
    public Vector3 Right { get; }
    public Vector3 Pivot { get; }

    /// <summary>The blade's reach from the pivot (m).</summary>
    public float Reach { get; }

    /// <summary>The item's arc relative to the standard 150° (scales the curve's angles about the strike angle, 0).</summary>
    public float ArcScale { get; }

    /// <summary>A swing's reach (m): the blade's length in cells, scaled by the size prefix, plus the arm.</summary>
    public static float SwingReach(float lengthCells, float size) => lengthCells * size * Units.CellSize + SwingArm;

    /// <summary>A thrust's full reach (m) along the aim.</summary>
    public static float ThrustReach(float lengthCells, float size) => lengthCells * size * Units.CellSize + ThrustArm;

    /// <summary>The blade direction at curve angle <paramref name="deg"/> (before the arc scaling).</summary>
    public Vector3 Direction(float deg)
    {
        float a = deg * ArcScale * MathF.PI / 180f;
        return Forward * MathF.Cos(a) + Up * MathF.Sin(a);
    }

    /// <summary>A point on the blade at distance <paramref name="r"/> from the pivot.</summary>
    public Vector3 Point(float deg, float r) => Pivot + Direction(deg) * r;

    /// <summary>
    /// Whether the blade, swept from angle <paramref name="fromDeg"/> to <paramref name="toDeg"/> (in ≤
    /// <see cref="StepDeg"/> steps), passes within <see cref="BladeHalfWidth"/> + <see cref="LateralTolerance"/> of
    /// the box; <paramref name="hitDeg"/> is the first angle that connects.
    /// </summary>
    public bool Sweeps(float fromDeg, float toDeg, Vector3 min, Vector3 max, out float hitDeg)
    {
        hitDeg = float.NaN;
        var nearest = Vector3.Clamp(Pivot, min, max);
        if (Vector3.Distance(Pivot, nearest) > Reach + BladeHalfWidth + LateralTolerance) return false; // out of reach
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(toDeg - fromDeg) * ArcScale / StepDeg));
        for (int i = 0; i <= steps; i++)
        {
            float deg = fromDeg + (toDeg - fromDeg) * i / steps;
            if (SegmentToBox(Pivot, Point(deg, Reach), min, max) <= BladeHalfWidth + LateralTolerance)
            {
                hitDeg = deg;
                return true;
            }
        }
        return false;
    }

    /// <summary>The shortest distance between a segment and a box (sampled along the segment every 5 cm).</summary>
    public static float SegmentToBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
    {
        int n = Math.Max(2, (int)MathF.Ceiling(Vector3.Distance(a, b) / 0.05f));
        float best = float.MaxValue;
        for (int i = 0; i <= n; i++)
        {
            var p = Vector3.Lerp(a, b, i / (float)n);
            best = MathF.Min(best, Vector3.Distance(p, Vector3.Clamp(p, min, max)));
            if (best <= 0f) break;
        }
        return best;
    }
}
