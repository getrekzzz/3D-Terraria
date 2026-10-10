using System.Numerics;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Physics;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Where a player's shot starts and which way it flies: out of the weapon hand (beside, below and a little ahead of the
/// eye, in the <see cref="PlayerEntity.Facing"/> frame) and towards the crosshair point, so arrows, bolts and thrown
/// weapons leave the hand and still land where the crosshair points. A hand inside (or behind) a wall falls back to
/// the eye, so a shot never starts inside terrain.
/// </summary>
public static class ShotGeometry
{
    /// <summary>The weapon hand relative to the eye in the facing frame (metres): X right, Y up, Z forward.</summary>
    public static readonly Vector3 HandOffset = new(0.22f, -0.3f, 0.45f);

    /// <summary>
    /// The nearest point (m from the eye along <see cref="PlayerEntity.Aim"/>) shots converge on: a closer crosshair
    /// point would send them steeply across the view instead of ahead.
    /// </summary>
    public const float MinConvergence = 2f;

    /// <summary>The weapon hand in world space (metres), or the eye when the hand is inside or behind a wall.</summary>
    public static Vector3 Origin(VoxelWorld world, PlayerEntity p) => Origin(world, p, HandOffset);

    /// <summary>
    /// The eye moved by <paramref name="offset"/> in the facing frame (X right, Y up, Z forward), or the eye itself when
    /// a solid cell or blocking object lies between the two, the offset point is in unloaded terrain, or the player
    /// looks straight up or down (no sideways axis).
    /// </summary>
    public static Vector3 Origin(VoxelWorld world, PlayerEntity p, Vector3 offset)
    {
        var eye = p.Eye;
        var forward = p.Facing;
        var side = Vector3.Cross(forward, Vector3.UnitY);
        if (side.LengthSquared() < 1e-6f || forward.LengthSquared() < 1e-6f) return eye;
        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(side);
        var up = Vector3.Cross(right, forward);
        var path = right * offset.X + up * offset.Y + forward * offset.Z;
        float length = path.Length();
        if (length < 1e-4f) return eye;
        var wanted = eye + path;
        if (world.GetBlock(VoxelMotion.CellOf(wanted)) == VoxelWorld.Unknown) return eye;
        var wall = Targeting.CastWhere(world, eye, path, length,
            (cell, block) => world.Content.Blocks[block].IsSolid || world.TileObjects.At(cell) is { BlocksMovement: true });
        return wall == null ? wanted : eye;
    }

    /// <summary>
    /// The direction (normalized) from <paramref name="origin"/> to the crosshair point: <see cref="PlayerEntity.Aim"/>
    /// at <see cref="PlayerEntity.AimDistance"/> from the eye, but never nearer than <see cref="MinConvergence"/>. Falls
    /// back to the aim when the origin sits on that point.
    /// </summary>
    public static Vector3 Direction(PlayerEntity p, Vector3 origin)
    {
        float distance = p.AimDistance > MinConvergence ? p.AimDistance : MinConvergence; // NaN clamps too
        var to = p.Eye + p.Aim * distance - origin;
        return to.LengthSquared() > 1e-6f ? Vector3.Normalize(to) : p.Aim;
    }
}
