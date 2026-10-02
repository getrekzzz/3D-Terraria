using System.Numerics;

namespace Strata.Core.Gameplay.Combat;

/// <summary>Aiming shots: gravity-bound projectiles are lobbed above the target so they drop onto it.</summary>
public static class Ballistics
{
    /// <summary>
    /// The launch direction from <paramref name="from"/> at <paramref name="speed"/> m/s that brings a projectile
    /// falling at <paramref name="gravity"/> m/s² onto <paramref name="to"/>: the aim point is raised by the drop
    /// over the straight-line flight time (close enough at the ranges creatures and residents shoot).
    /// </summary>
    public static Vector3 Aim(Vector3 from, Vector3 to, float speed, float gravity)
    {
        float flight = Vector3.Distance(from, to) / MathF.Max(speed, 0.1f);
        to.Y += 0.5f * gravity * flight * flight;
        var d = to - from;
        return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.UnitZ;
    }
}
