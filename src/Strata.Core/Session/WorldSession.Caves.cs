using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>
    /// Finds an open cave floor in a world layer near a column: chunks are read when loaded and generated (without
    /// installing) otherwise, spiralling out from <paramref name="near"/>. A spot needs ground, no liquid, and
    /// <paramref name="headroom"/> open cells above. Used by <c>tp cave</c> and screenshot tours.
    /// </summary>
    public Int3? FindCave(Int3 near, WorldLayer layer, int headroom = 8, int radiusChunks = 2)
    {
        var bands = Generator.Plan.Bands;
        var (yMin, yMax) = bands.CaveRange(layer);
        var home = ChunkMath.CellToChunk(near);
        int cyMin = Math.Max(0, yMin >> ChunkMath.Shift), cyMax = Math.Min(World.ChunkCounts.Y - 1, yMax >> ChunkMath.Shift);
        for (int r = 0; r <= radiusChunks; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    for (int cy = cyMax; cy >= cyMin; cy--)
                    {
                        var coord = new Int3(home.X + dx, cy, home.Z + dz);
                        if (!World.ChunkInBounds(coord)) continue;
                        var data = World.GetChunk(coord)?.Capture() ?? Generator.Generate(coord).Data;
                        var spot = OpenFloor(data, coord, headroom, yMin, yMax);
                        if (spot != null) return spot;
                    }
                }
        return null;
    }

    /// <summary>The most open floor cell in a chunk (only columns whose headroom stays inside the chunk).</summary>
    private Int3? OpenFloor(ChunkData data, Int3 coord, int headroom, int yMin, int yMax)
    {
        var origin = coord * ChunkMath.Size;
        Int3? best = null;
        int bestRoom = headroom - 1;
        for (int z = 2; z < ChunkMath.Size - 2; z += 2)
            for (int x = 2; x < ChunkMath.Size - 2; x += 2)
                for (int y = 1; y < ChunkMath.Size - headroom; y++)
                {
                    int abs = origin.Y + y;
                    if (abs < yMin || abs > yMax || !Content.Blocks[data.GetBlock(ChunkMath.Index(x, y - 1, z))].IsSolid) continue;
                    int room = 0;
                    while (y + room < ChunkMath.Size)
                    {
                        int i = ChunkMath.Index(x, y + room, z);
                        if (Content.Blocks[data.GetBlock(i)].IsSolid || (data.Liquids?[i] ?? 0) != 0) break;
                        room++;
                    }
                    if (room > bestRoom)
                    {
                        bestRoom = room;
                        best = origin + new Int3(x, y, z);
                    }
                }
        return best;
    }
}
