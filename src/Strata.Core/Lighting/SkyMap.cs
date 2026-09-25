using System.Collections.Concurrent;
using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Voxels;

namespace Strata.Core.Lighting;

/// <summary>
/// Per-column sky exposure: <c>SkyTop(x, z)</c> is the lowest y such that every cell at or above it in the
/// column lets sunlight through unattenuated. Maintained from loaded chunk data (top-down scan); columns
/// whose upper chunks are not loaded fall back to an estimate from the world plan.
/// Tiles are published copy-on-write, so mesh jobs read them lock-free.
/// </summary>
public sealed class SkyMap
{
    private readonly ConcurrentDictionary<long, int[]> _tiles = new();
    private readonly VoxelWorld _world;
    private readonly RenderTables _tables;
    private readonly Func<int, int, int> _estimate;
    private readonly Func<int, int, int> _columnTopChunk;

    /// <param name="estimate">Fallback sky top for a column when data above is not loaded (e.g. terrain surface).</param>
    /// <param name="columnTopChunk">Highest chunk y that can contain non-air content in a chunk column.</param>
    public SkyMap(VoxelWorld world, RenderTables tables, Func<int, int, int> estimate, Func<int, int, int> columnTopChunk)
    {
        _world = world;
        _tables = tables;
        _estimate = estimate;
        _columnTopChunk = columnTopChunk;
    }

    private static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

    public int SkyTop(int x, int z)
    {
        int tx = x >> ChunkMath.Shift, tz = z >> ChunkMath.Shift;
        if (_tiles.TryGetValue(Key(tx, tz), out var tile)) return tile[(z & ChunkMath.Mask) * ChunkMath.Size + (x & ChunkMath.Mask)];
        return _estimate(x, z);
    }

    /// <summary>Rescans a chunk column. Returns (column index, old, new) for every column whose value changed.</summary>
    public List<(int X, int Z, int Old, int New)> Rebuild(int chunkX, int chunkZ)
    {
        var changes = new List<(int, int, int, int)>();
        var key = Key(chunkX, chunkZ);
        _tiles.TryGetValue(key, out var old);
        var tile = new int[ChunkMath.Area];
        int ox = chunkX << ChunkMath.Shift, oz = chunkZ << ChunkMath.Shift;
        int topChunk = Math.Max(_columnTopChunk(chunkX, chunkZ), HighestLoaded(chunkX, chunkZ));
        for (int lz = 0; lz < ChunkMath.Size; lz++)
            for (int lx = 0; lx < ChunkMath.Size; lx++)
            {
                int value = ScanColumn(ox + lx, oz + lz, chunkX, chunkZ, topChunk);
                int i = lz * ChunkMath.Size + lx;
                tile[i] = value;
                int previous = old?[i] ?? _estimate(ox + lx, oz + lz);
                if (previous != value) changes.Add((ox + lx, oz + lz, previous, value));
            }
        _tiles[key] = tile;
        return changes;
    }

    public void Forget(int chunkX, int chunkZ) => _tiles.TryRemove(Key(chunkX, chunkZ), out _);

    private int HighestLoaded(int cx, int cz)
    {
        for (int cy = _world.ChunkCounts.Y - 1; cy >= 0; cy--)
            if (_world.HasChunk(new Int3(cx, cy, cz))) return cy;
        return -1;
    }

    private int ScanColumn(int x, int z, int cx, int cz, int topChunk)
    {
        int lx = x & ChunkMath.Mask, lz = z & ChunkMath.Mask;
        for (int cy = topChunk; cy >= 0; cy--)
        {
            var chunk = _world.GetChunk(new Int3(cx, cy, cz));
            if (chunk == null)
            {
                // Unknown content below: fall back to the estimate (never above the known-open part).
                return Math.Min(_estimate(x, z), (cy + 1) << ChunkMath.Shift);
            }
            var data = chunk.Capture();
            for (int ly = ChunkMath.Size - 1; ly >= 0; ly--)
            {
                int i = ChunkMath.Index(lx, ly, lz);
                if (_tables.LightFilter[data.GetBlock(i)] != 0 || (data.Liquids != null && data.Liquids[i] != 0) || (data.Shapes != null && data.Shapes[i] != 0))
                    return (cy << ChunkMath.Shift) + ly + 1;
            }
        }
        return 0;
    }
}
