using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Building;

/// <summary>
/// The building grid: outside precision mode blocks are mined and placed as aligned 2×2×2-cell cubes (1 m), so the
/// cursor shows the same cube for any point inside it and a pickaxe digs a shaft the player fits through. Crisp
/// shapes scale up to the cube (a 1 m ramp is two ramp cells over a full cell); precision mode works on single cells.
/// The ground is shaped in half-block cells, so a block may also be <i>raised</i>: one cell above the grid vertically
/// (still aligned across), to sit whole on ground that is off the grid. Its cells carry <see cref="CellFlags.HalfStep"/>.
/// </summary>
public static class BlockGrid
{
    /// <summary>Edge of one grid block in cells.</summary>
    public const int Size = 2;

    /// <summary>Minimum cell of the grid block (or the <paramref name="raised"/> block) containing <paramref name="cell"/>.</summary>
    public static Int3 MinOf(Int3 cell, bool raised = false) =>
        new(Floor(cell.X), raised ? Floor(cell.Y - 1) + 1 : Floor(cell.Y), Floor(cell.Z));

    /// <summary>The cells of the grid block (or the <paramref name="raised"/> block) containing <paramref name="cell"/>, bottom layer first.</summary>
    public static IEnumerable<Int3> Cells(Int3 cell, bool raised = false)
    {
        var min = MinOf(cell, raised);
        for (int y = 0; y < Size; y++)
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                    yield return min + new Int3(x, y, z);
    }

    /// <summary>
    /// The cells (and their shape codes) that make <paramref name="shape"/> at block scale, turned by
    /// <paramref name="rotation"/> quarter turns like single-cell shapes (ramps rise towards the rotation direction,
    /// panels stand on that side). Cells the shape leaves open are not listed. Bottom layer first.
    /// </summary>
    public static List<(Int3 Cell, byte Shape)> Pattern(Int3 cell, CellShape shape, int rotation, bool raised = false)
    {
        var min = MinOf(cell, raised);
        var list = new List<(Int3, byte)>(8);
        for (int y = 0; y < Size; y++)
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    if (Local(shape, x, y, z) is not { } local) continue;
                    // Rotate the local cell like ShapeGeometry rotates points: (x, z) -> (1 - z, x) per quarter turn.
                    int rx = x, rz = z;
                    for (int i = 0; i < (rotation & 3); i++) (rx, rz) = (Size - 1 - rz, rx);
                    list.Add((min + new Int3(rx, y, rz), ShapeCode.Pack(local, rotation)));
                }
        return list;
    }

    /// <summary>Is <paramref name="cell"/> part of a raised block?</summary>
    public static bool IsRaised(VoxelWorld world, Int3 cell) => (world.GetFlags(cell.X, cell.Y, cell.Z) & CellFlags.HalfStep) != 0;

    /// <summary>
    /// The block <paramref name="cell"/> belongs to, as mining and hammering see it: a raised block's own cells, or the
    /// grid block's cells minus any that belong to a raised block (so digging next to a raised block never cuts it).
    /// </summary>
    public static IEnumerable<Int3> BlockOf(VoxelWorld world, Int3 cell)
    {
        bool raised = IsRaised(world, cell);
        return Cells(cell, raised).Where(c => IsRaised(world, c) == raised);
    }

    /// <summary>
    /// Whether a block placed into <paramref name="cell"/> goes raised: whichever of the grid block and the raised block
    /// containing the cell has more room is filled, so a 1 m block on ground half a block off the grid comes out whole
    /// instead of cut in half. On a tie it follows the block that was <paramref name="hit"/>, so a wall keeps its height.
    /// </summary>
    public static bool PlaceRaised(VoxelWorld world, Int3 cell, Int3 hit, CellShape shape, int rotation)
    {
        int onGrid = Room(world, Pattern(cell, shape, rotation));
        int halfUp = Room(world, Pattern(cell, shape, rotation, raised: true));
        return halfUp != onGrid ? halfUp > onGrid : IsRaised(world, hit);
    }

    /// <summary>How many of the cells are empty (no block, no solid object).</summary>
    private static int Room(VoxelWorld world, List<(Int3 Cell, byte Shape)> cells) =>
        cells.Count(c => world.GetBlock(c.Cell) == 0 && world.TileObjects.At(c.Cell) is not { Def.Decor: false });

    /// <summary>The shape of one cell of an unrotated block shape (high side / panel side towards +Z), or null if open.</summary>
    private static CellShape? Local(CellShape shape, int x, int y, int z)
    {
        bool top = y == 1, back = z == 1;
        return shape switch
        {
            CellShape.HalfBottom => top ? null : CellShape.Full,
            CellShape.HalfTop => top ? CellShape.Full : null,
            CellShape.Slope => (top, back) switch
            {
                (false, true) => CellShape.Full,
                (true, false) => null,
                _ => CellShape.Slope,
            },
            CellShape.SlopeCeiling => (top, back) switch
            {
                (true, true) => CellShape.Full,
                (false, false) => null,
                _ => CellShape.SlopeCeiling,
            },
            CellShape.Stair => !top || back ? CellShape.Full : null,
            CellShape.StairCeiling => top || back ? CellShape.Full : null,
            CellShape.Panel => back ? CellShape.Panel : null,
            CellShape.Platform => top ? CellShape.Platform : null,
            CellShape.Post => x == 0 && z == 0 ? CellShape.Full : null,
            _ => CellShape.Full,
        };
    }

    private static int Floor(int v) => v - ((v % Size) + Size) % Size;
}
