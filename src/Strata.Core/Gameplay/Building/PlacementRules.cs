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

    /// <summary>
    /// Checks placing a block into several cells at once (a grid block). Returns the free cells that would be filled
    /// (at most <paramref name="limit"/>): the group needs support from one of them and none of them may overlap a
    /// blocker. Occupied cells are simply skipped.
    /// </summary>
    public static (PlacementResult Result, List<(Int3 Cell, byte Shape)> Free) CanPlaceBlocks(VoxelWorld world,
        IReadOnlyList<(Int3 Cell, byte Shape)> cells, BlockDef block, int limit, IEnumerable<(System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)> blockers)
    {
        var free = new List<(Int3 Cell, byte Shape)>();
        var worst = PlacementResult.Occupied;
        bool supported = false;
        var boxes = blockers as IList<(System.Numerics.Vector3, System.Numerics.Vector3)> ?? blockers.ToList();
        foreach (var (cell, shape) in cells)
        {
            if (free.Count >= limit) break;
            var r = CanPlaceBlock(world, cell, block, boxes);
            if (r == PlacementResult.NoSupport) r = PlacementResult.Ok; // the group is supported as a whole
            else if (r == PlacementResult.Ok) supported = true;
            if (r is PlacementResult.BlockedByEntity or PlacementResult.Unloaded) return (r, new List<(Int3, byte)>());
            if (r != PlacementResult.Ok)
            {
                if (r != PlacementResult.Occupied) worst = r;
                continue;
            }
            free.Add((cell, shape));
        }
        if (free.Count == 0) return (worst, free);
        return supported ? (PlacementResult.Ok, free) : (PlacementResult.NoSupport, new List<(Int3, byte)>());
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
        return Supported(world, def.Anchor, box, strict: true) ? PlacementResult.Ok : PlacementResult.NoSupport;
    }

    /// <summary>
    /// Is a placed object still held up? Placement is strict (the whole footprint must rest on solid ground);
    /// existing objects only fall once <i>nothing</i> holds them.
    /// </summary>
    public static bool IsSupported(VoxelWorld world, TileObjectInstance obj) => Supported(world, obj.Def.Anchor, obj.Bounds, strict: false);

    private static bool Supported(VoxelWorld world, TileAnchor anchor, Aabb3i box, bool strict)
    {
        bool Floor() => Solid(world, box.Min.X, box.Max.X, box.Min.Y - 1, box.Min.Z, box.Max.Z, strict);
        bool Ceiling() => Solid(world, box.Min.X, box.Max.X, box.Max.Y, box.Min.Z, box.Max.Z, strict);
        return anchor switch
        {
            TileAnchor.Floor => Floor(),
            TileAnchor.Ceiling => Ceiling(),
            TileAnchor.Wall => TouchesWall(world, box),
            TileAnchor.FloorOrWall => Floor() || TouchesWall(world, box),
            TileAnchor.Any => Floor() || Ceiling() || TouchesWall(world, box),
            _ => true,
        };
    }

    /// <summary>All (strict) or any (relaxed) cells of a horizontal slab are solid or hold another object.</summary>
    private static bool Solid(VoxelWorld world, int x0, int x1, int y, int z0, int z1, bool all)
    {
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                bool solid = world.IsSolid(x, y, z) || world.TileObjects.IsOccupied(new Int3(x, y, z));
                if (all && !solid) return false;
                if (!all && solid) return true;
            }
        return all;
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
