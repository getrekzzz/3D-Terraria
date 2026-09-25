using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Physics;

/// <summary>
/// Axis-separated AABB movement against solid voxels and blocking tile objects such as closed doors (dropped items,
/// creatures, projectiles). Positions are in meters (box centre), sizes are half-extents in meters.
/// </summary>
public static class VoxelMotion
{
    private const float Skin = 0.001f;

    /// <summary>Moves a box by velocity·dt, zeroing velocity components that hit terrain. Returns true when resting on ground.</summary>
    public static bool Move(VoxelWorld world, ref Vector3 position, ref Vector3 velocity, Vector3 halfExtents, float dt)
    {
        bool grounded = false;
        var delta = velocity * dt;
        for (int axis = 0; axis < 3; axis++)
        {
            float d = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
            if (d == 0f) continue;
            var moved = position;
            Set(ref moved, axis, Get(position, axis) + d);
            if (Overlaps(world, moved, halfExtents))
            {
                // Clamp to the cell boundary in the direction of motion.
                float edge = d > 0
                    ? MathF.Floor((Get(moved, axis) + Get(halfExtents, axis)) / Units.CellSize) * Units.CellSize - Get(halfExtents, axis) - Skin
                    : MathF.Ceiling((Get(moved, axis) - Get(halfExtents, axis)) / Units.CellSize) * Units.CellSize + Get(halfExtents, axis) + Skin;
                Set(ref moved, axis, edge);
                if (Overlaps(world, moved, halfExtents)) moved = position;
                if (axis == 1 && d < 0) grounded = true;
                Set(ref velocity, axis, 0f);
            }
            position = moved;
        }
        return grounded;
    }

    public static bool Overlaps(VoxelWorld world, Vector3 center, Vector3 half)
    {
        var min = (center - half) / Units.CellSize;
        var max = (center + half) / Units.CellSize;
        int x0 = (int)MathF.Floor(min.X), y0 = (int)MathF.Floor(min.Y), z0 = (int)MathF.Floor(min.Z);
        int x1 = (int)MathF.Ceiling(max.X), y1 = (int)MathF.Ceiling(max.Y), z1 = (int)MathF.Ceiling(max.Z);
        for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    if (world.IsSolid(x, y, z) || world.TileObjects.At(new Int3(x, y, z)) is { BlocksMovement: true }) return true;
        return false;
    }

    public static Int3 CellOf(Vector3 meters) =>
        new((int)MathF.Floor(meters.X / Units.CellSize), (int)MathF.Floor(meters.Y / Units.CellSize), (int)MathF.Floor(meters.Z / Units.CellSize));

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static void Set(ref Vector3 v, int axis, float value)
    {
        if (axis == 0) v.X = value;
        else if (axis == 1) v.Y = value;
        else v.Z = value;
    }
}
