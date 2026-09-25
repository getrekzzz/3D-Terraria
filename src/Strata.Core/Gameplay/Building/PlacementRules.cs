using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Building;

public enum PlacementResult
{
    Ok,
    OutOfWorld,
    Occupied,
    NoSupport,
    BlockedByEntity,
    Unloaded,
}

/// <summary>Where blocks and tile objects may be placed.</summary>
public static class PlacementRules
{
    /// <summary>Checks a block placement. <paramref name="blockers"/> are entity boxes in cell units (min inclusive, max exclusive, floats).</summary>
    public static PlacementResult CanPlaceBlock(VoxelWorld world, Int3 cell, BlockDef block, IEnumerable<(System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)> blockers)
    {
        if (!world.InBounds(cell.X, cell.Y, cell.Z)) return PlacementResult.OutOfWorld;
        ushort current = world.GetBlock(cell);
        if (current == VoxelWorld.Unknown) return PlacementResult.Unloaded;
        if (current != 0) return PlacementResult.Occupied;
        var obj = world.TileObjects.At(cell);
        if (obj != null && !obj.Def.Decor) return PlacementResult.Occupied;
        if (!HasSupport(world, cell)) return PlacementResult.NoSupport;
        if (block.IsSolid)
            foreach (var (min, max) in blockers)
                if (cell.X < max.X && cell.X + 1 > min.X && cell.Y < max.Y && cell.Y + 1 > min.Y && cell.Z < max.Z && cell.Z + 1 > min.Z)
                    return PlacementResult.BlockedByEntity;
        return PlacementResult.Ok;
    }

    /// <summary>Blocks must touch an existing block, tile object or liquid (no floating placement).</summary>
    public static bool HasSupport(VoxelWorld world, Int3 cell)
    {
        foreach (var o in Faces.Offsets)
        {
            var n = cell + o;
            ushort b = world.GetBlock(n);
            if (b != 0 && b != VoxelWorld.Unknown) return true;
            if (world.GetLiquid(n.X, n.Y, n.Z) != 0 || world.TileObjects.IsOccupied(n)) return true;
        }
        return false;
    }

    public static PlacementResult CanPlaceObject(VoxelWorld world, TileObjectDef def, Int3 anchor, int rotation)
    {
        var size = TileObjectInstance.FootprintOf(def, rotation);
        var box = new Aabb3i(anchor, anchor + size);
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                {
                    if (!world.InBounds(x, y, z)) return PlacementResult.OutOfWorld;
                    ushort b = world.GetBlock(x, y, z);
                    if (b == VoxelWorld.Unknown) return PlacementResult.Unloaded;
                    if (b != 0 || world.TileObjects.IsOccupied(new Int3(x, y, z))) return PlacementResult.Occupied;
                }
        bool floor = AllSolid(world, box.Min.X, box.Max.X, box.Min.Y - 1, box.Min.Z, box.Max.Z);
        bool ceiling = AllSolid(world, box.Min.X, box.Max.X, box.Max.Y, box.Min.Z, box.Max.Z);
        bool wall = TouchesWall(world, box);
        bool ok = def.Anchor switch
        {
            TileAnchor.Floor => floor,
            TileAnchor.Ceiling => ceiling,
            TileAnchor.Wall => wall,
            TileAnchor.FloorOrWall => floor || wall,
            TileAnchor.Any => floor || ceiling || wall,
            _ => true,
        };
        return ok ? PlacementResult.Ok : PlacementResult.NoSupport;
    }

    private static bool AllSolid(VoxelWorld world, int x0, int x1, int y, int z0, int z1)
    {
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
                if (!world.IsSolid(x, y, z) && !world.TileObjects.IsOccupied(new Int3(x, y, z))) return false;
        return true;
    }

    private static bool TouchesWall(VoxelWorld world, Aabb3i box)
    {
        for (int y = box.Min.Y; y < box.Max.Y; y++)
        {
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                if (world.IsSolid(box.Min.X - 1, y, z) || world.IsSolid(box.Max.X, y, z)) return true;
            for (int x = box.Min.X; x < box.Max.X; x++)
                if (world.IsSolid(x, y, box.Min.Z - 1) || world.IsSolid(x, y, box.Max.Z)) return true;
        }
        return false;
    }
}
