using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>What the crosshair points at.</summary>
public sealed record Target(Int3 Cell, Int3 Normal, ushort Block, TileObjectInstance? Object, Vector3 HitCells, float Distance)
{
    /// <summary>Hit position within the cell, each axis 0–1 (drives brush alignment).</summary>
    public Vector3 Fraction => HitCells - new Vector3(Cell.X, Cell.Y, Cell.Z);

    /// <summary>The empty cell in front of the hit face (placement target).</summary>
    public Int3 Adjacent => Cell + Normal;

    public bool IsObject => Object != null;
}

/// <summary>Voxel-exact targeting: DDA through cells, stopping at solid (or non-air) blocks and tile objects.</summary>
public static class Targeting
{
    /// <summary>
    /// Casts from <paramref name="originMeters"/> along <paramref name="direction"/> up to <paramref name="reachMeters"/>.
    /// Liquids are ignored; decor objects are hit only when <paramref name="includeDecor"/> is set.
    /// </summary>
    public static Target? Cast(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters, bool includeDecor = true)
    {
        if (direction.LengthSquared() < 1e-10f) return null;
        direction = Vector3.Normalize(direction);
        var origin = originMeters / Units.CellSize;
        float reach = reachMeters / Units.CellSize;
        TileObjectInstance? obj = null;
        var hit = Cast(world, origin, direction, reach, (cell, block) =>
        {
            if (block != 0 && world.Content.Blocks[block].Render != Content.Defs.BlockRender.None) return true;
            var o = world.TileObjects.At(cell);
            if (o == null || (o.Def.Decor && !includeDecor)) return false;
            obj = o;
            return true;
        });
        if (hit == null) return null;
        var (cell, normal, t, blockId) = hit.Value;
        return new Target(cell, normal, blockId, obj, origin + direction * t, t * Units.CellSize);
    }

    /// <summary>
    /// Walks the ray for a bucket: the first liquid cell (<c>Liquid</c> true) or, when the ray meets a
    /// solid block or runs out of reach first, the last open cell before it (where poured liquid would go).
    /// </summary>
    public static (Int3 Cell, bool Liquid)? CastLiquid(VoxelWorld world, Vector3 originMeters, Vector3 direction, float reachMeters)
    {
        if (direction.LengthSquared() < 1e-10f) return null;
        direction = Vector3.Normalize(direction);
        var origin = originMeters / Units.CellSize;
        float reach = reachMeters / Units.CellSize;
        var content = world.Content;
        Int3? lastOpen = null;
        var hit = Cast(world, origin, direction, reach, (cell, block) =>
        {
            if (world.GetLiquid(cell.X, cell.Y, cell.Z) != 0) return true;
            if (block != 0 && content.Blocks[block].IsSolid) return true;
            lastOpen = cell;
            return false;
        });
        if (hit is { } h && world.GetLiquid(h.Cell.X, h.Cell.Y, h.Cell.Z) != 0) return (h.Cell, true);
        if (hit == null)
        {
            // Out of reach without hitting anything: pour at the end of the reach.
            var end = origin + direction * reach;
            var cell = new Int3((int)MathF.Floor(end.X), (int)MathF.Floor(end.Y), (int)MathF.Floor(end.Z));
            return world.GetBlock(cell) == VoxelWorld.Unknown ? null : (cell, false);
        }
        return lastOpen is { } open ? (open, false) : null;
    }

    private static (Int3 Cell, Int3 Normal, float T, ushort Block)? Cast(VoxelWorld world, Vector3 origin, Vector3 dir, float maxT, Func<Int3, ushort, bool> isHit)
    {
        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y), sz = Math.Sign(dir.Z);
        float dx = sx != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
        float dy = sy != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;
        float dz = sz != 0 ? MathF.Abs(1f / dir.Z) : float.PositiveInfinity;
        float tx = sx != 0 ? (sx > 0 ? x + 1 - origin.X : origin.X - x) * dx : float.PositiveInfinity;
        float ty = sy != 0 ? (sy > 0 ? y + 1 - origin.Y : origin.Y - y) * dy : float.PositiveInfinity;
        float tz = sz != 0 ? (sz > 0 ? z + 1 - origin.Z : origin.Z - z) * dz : float.PositiveInfinity;
        var normal = Int3.Zero;
        float t = 0;
        while (t <= maxT)
        {
            var cell = new Int3(x, y, z);
            ushort block = world.GetBlock(cell);
            if (block == VoxelWorld.Unknown) return null;
            if (isHit(cell, block)) return (cell, normal, t, block);
            if (tx < ty && tx < tz) { x += sx; t = tx; tx += dx; normal = new Int3(-sx, 0, 0); }
            else if (ty < tz) { y += sy; t = ty; ty += dy; normal = new Int3(0, -sy, 0); }
            else { z += sz; t = tz; tz += dz; normal = new Int3(0, 0, -sz); }
        }
        return null;
    }
}
