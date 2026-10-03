using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Items;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class WorldEditor
{
    /// <summary>
    /// Places a block into several cells at once (a grid block): every listed cell that is free gets the block, in one
    /// edit. At most <paramref name="limit"/> cells are filled (the items the player holds). The group needs support
    /// from at least one of its cells and none of the filled cells may overlap a <paramref name="blockers"/> box.
    /// Does not consume items; returns how many cells were filled. <paramref name="flags"/> are set on the filled cells
    /// besides <see cref="CellFlags.PlayerPlaced"/> (<see cref="CellFlags.HalfStep"/> for a raised block).
    /// </summary>
    public (PlacementResult Result, int Placed) PlaceBlocks(IReadOnlyList<(Int3 Cell, byte Shape)> cells, BlockDef block, int limit,
        IEnumerable<(Vector3 Min, Vector3 Max)> blockers, CellFlags flags = CellFlags.None)
    {
        var (result, free) = PlacementRules.CanPlaceBlocks(World, cells, block, limit, blockers);
        if (result != PlacementResult.Ok) return (result, 0);

        var batch = World.BeginEdit();
        var opened = new List<Int3>();
        foreach (var (cell, shape) in free)
        {
            if (World.TileObjects.At(cell) is { } decor) BreakObject(decor, dropItems: true);
            batch.SetBlock(cell, (ushort)block.RuntimeId, block.Render == BlockRender.Crisp ? shape : (byte)0, CellFlags.PlayerPlaced | flags);
            if (block.IsSolid) batch.SetLiquid(cell, 0);
        }
        if (block.Falls)
            foreach (var (cell, _) in free)
                if (World.GetBlock(cell + Int3.Down) == 0 && !free.Any(f => f.Cell == cell + Int3.Down)) opened.Add(cell);
        FallingBlocks.Settle(batch, _session.Content, opened);
        Commit(batch);
        foreach (var (cell, _) in free) BlockPlaced?.Invoke(cell, block);
        return (PlacementResult.Ok, free.Count);
    }

    /// <summary>
    /// Hammers a grid block into a block-scale shape: its crisp cells take the pattern's shapes and crisp cells the
    /// pattern leaves open are knocked out (dropping their item). A raised block is shaped as itself. Returns false when
    /// nothing in the block could change.
    /// </summary>
    public bool ReshapeBlock(Int3 cell, CellShape shape, int rotation)
    {
        var content = _session.Content;
        var pattern = BlockGrid.Pattern(cell, shape, rotation, BlockGrid.IsRaised(World, cell)).ToDictionary(p => p.Cell, p => p.Shape);
        var batch = World.BeginEdit();
        var removed = new List<Int3>();
        bool changed = false;
        foreach (var c in BlockGrid.BlockOf(World, cell))
        {
            ushort id = World.GetBlock(c);
            if (id == 0 || id == VoxelWorld.Unknown || content.Blocks[id].Render != BlockRender.Crisp || IsLocked(c)) continue;
            if (pattern.TryGetValue(c, out byte code))
            {
                if (World.GetShape(c.X, c.Y, c.Z) == code) continue;
                batch.SetBlock(c, id, code, World.GetFlags(c.X, c.Y, c.Z));
            }
            else
            {
                batch.SetBlock(c, 0);
                removed.Add(c);
                if (content.BlockDrops[id] is { } drop) SpawnDrop(new ItemStack(drop, 1), c);
            }
            changed = true;
        }
        if (!changed) return false;
        Commit(batch);
        if (removed.Count > 0) BreakUnsupportedAround(removed, new List<Voxels.TileObjects.TileObjectInstance>());
        return true;
    }
}
