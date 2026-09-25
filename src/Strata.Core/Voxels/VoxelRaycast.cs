using System.Numerics;
using Strata.Core.Common;

namespace Strata.Core.Voxels;

/// <summary>Result of a grid ray march.</summary>
public readonly record struct VoxelHit(Int3 Cell, Int3 Normal, float Distance, ushort Block);

/// <summary>Amanatides–Woo DDA through the cell grid. Positions are in <b>cell units</b>.</summary>
public static class VoxelRaycast
{
    /// <summary>
    /// Marches from <paramref name="origin"/> along <paramref name="direction"/> up to <paramref name="maxDistance"/> cells,
    /// returning the first cell for which <paramref name="isHit"/> is true.
    /// </summary>
    public static VoxelHit? Cast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, Func<ushort, bool> isHit)
    {
        if (direction.LengthSquared() < 1e-12f) return null;
        direction = Vector3.Normalize(direction);

        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int stepX = Math.Sign(direction.X), stepY = Math.Sign(direction.Y), stepZ = Math.Sign(direction.Z);
        float tDeltaX = stepX != 0 ? MathF.Abs(1f / direction.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? MathF.Abs(1f / direction.Y) : float.PositiveInfinity;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / direction.Z) : float.PositiveInfinity;
        float tMaxX = stepX != 0 ? (stepX > 0 ? x + 1 - origin.X : origin.X - x) * tDeltaX : float.PositiveInfinity;
        float tMaxY = stepY != 0 ? (stepY > 0 ? y + 1 - origin.Y : origin.Y - y) * tDeltaY : float.PositiveInfinity;
        float tMaxZ = stepZ != 0 ? (stepZ > 0 ? z + 1 - origin.Z : origin.Z - z) * tDeltaZ : float.PositiveInfinity;

        var normal = Int3.Zero;
        float t = 0f;
        while (t <= maxDistance)
        {
            ushort block = world.GetBlock(x, y, z);
            if (block == VoxelWorld.Unknown) return null;
            if (isHit(block)) return new VoxelHit(new Int3(x, y, z), normal, t, block);

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                x += stepX; t = tMaxX; tMaxX += tDeltaX; normal = new Int3(-stepX, 0, 0);
            }
            else if (tMaxY < tMaxZ)
            {
                y += stepY; t = tMaxY; tMaxY += tDeltaY; normal = new Int3(0, -stepY, 0);
            }
            else
            {
                z += stepZ; t = tMaxZ; tMaxZ += tDeltaZ; normal = new Int3(0, 0, -stepZ);
            }
        }
        return null;
    }
}
