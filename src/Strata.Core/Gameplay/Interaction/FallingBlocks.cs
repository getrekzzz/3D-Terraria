using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// Gravity for loose blocks (sand, silt, snow…): when the cell below a falling block opens up, the column of
/// falling blocks above it drops down to rest on the next solid cell.
/// </summary>
public static class FallingBlocks
{
    /// <summary>Maximum column height scanned above an opening.</summary>
    public const int MaxColumn = 96;

    /// <summary>Largest open gap above the lowest opened cell that is looked through for loose blocks.</summary>
    public const int MaxGap = 8;

    /// <summary>Settles falling blocks above each opened cell into <paramref name="batch"/>. Returns the number of moved blocks.</summary>
    public static int Settle(EditBatch batch, ContentDatabase content, IEnumerable<Int3> openedCells)
    {
        int moved = 0;
        var columns = new Dictionary<(int X, int Z), int>();
        foreach (var c in openedCells)
            columns[(c.X, c.Z)] = columns.TryGetValue((c.X, c.Z), out int y) ? Math.Min(y, c.Y) : c.Y;

        foreach (var ((x, z), yOpen) in columns)
        {
            // Find the floor: lowest open cell of the gap containing yOpen.
            int write = yOpen;
            while (write > 0 && batch.GetBlock(x, write - 1, z) == 0) write--;
            bool inRun = false;
            for (int y = yOpen; y < yOpen + MaxColumn; y++)
            {
                ushort b = batch.GetBlock(x, y, z);
                if (b == VoxelWorld.Unknown) break;
                if (b == 0)
                {
                    // Skip the freshly opened gap (a brush opens several cells) but stop above a fallen run.
                    if (inRun || y - yOpen > MaxGap) break;
                    continue;
                }
                if (!content.Blocks[b].Falls) break;
                inRun = true;
                if (y > write)
                {
                    batch.SetBlock(x, write, z, b);
                    batch.SetBlock(x, y, z, 0);
                    moved++;
                }
                write++;
            }
        }
        return moved;
    }
}
