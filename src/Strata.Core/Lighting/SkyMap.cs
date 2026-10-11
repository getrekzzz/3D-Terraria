using System.Collections.Concurrent;
using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Voxels;

namespace Strata.Core.Lighting;

/// <summary>
/// Per-column sky exposure: <c>SkyTop(x, z)</c> is the lowest y such that every cell at or above it in the
/// column lets sunlight through unattenuated. <c>ShadeTop(x, z)</c> extends it for open air under a floating roof (a
/// sky island, an arch, a high canopy): when the first blocking cell floats above the terrain surface with a tall
/// air gap below it, the air from the ground up to the roof counts as outdoor shade (lit, but less than open sky);
/// otherwise it equals <c>SkyTop</c>. Maintained from loaded chunk data (top-down scan); columns whose upper chunks
/// are not loaded fall back to an estimate from the world plan. Tiles are published copy-on-write, so mesh jobs read
/// them lock-free.
/// </summary>
public sealed class SkyMap
{
    private readonly ConcurrentDictionary<long, int[]> _tiles = new();
    private readonly VoxelWorld _world;
    private readonly RenderTables _tables;
    private readonly Func<int, int, int> _estimate;
    private readonly Func<int, int, int> _columnTopChunk;
    private readonly Func<int, int, int>? _roof;

    /// <param name="estimate">Fallback sky top for a column when data above is not loaded (e.g. terrain surface).</param>
    /// <param name="columnTopChunk">Highest chunk y that can contain non-air content in a chunk column.</param>
    /// <param name="roof">Don't Dig Up: the lowest cell of the rock roof over the bottom surface per column. The world
    /// then has no open sky at all (<see cref="SkyTop"/> is the world's height); the air between the ground and the roof
    /// is lit as outdoor shade (<see cref="ShadeTop"/> up to <see cref="ShadeCap"/>).</param>
    public SkyMap(VoxelWorld world, RenderTables tables, Func<int, int, int> estimate, Func<int, int, int> columnTopChunk,
        Func<int, int, int>? roof = null)
    {
        _world = world;
        _tables = tables;
        _estimate = estimate;
        _columnTopChunk = columnTopChunk;
        _roof = roof;
    }

    /// <summary>Whether the world's open air is a pocket under a rock roof (Don't Dig Up).</summary>
    public bool HasRoof => _roof != null;

    /// <summary>Upper bound (exclusive) of outdoor shade in a column: the roof's underside, or no bound.</summary>
    public int ShadeCap(int x, int z) => _roof?.Invoke(x, z) ?? int.MaxValue;

    /// <summary>Lowest y of the open air a player stands in outdoors: the sky top, or under a roof the shade top.</summary>
    public int OpenTop(int x, int z) => _roof != null ? ShadeTop(x, z) : SkyTop(x, z);

    /// <summary>Minimum air gap (cells) under a floating roof for the ground below to count as outdoor shade.</summary>
    public const int ShadeGap = 12;

    /// <summary>A roof must start at least this far above the terrain surface estimate to count as floating.</summary>
    public const int ShadeMargin = 3;

    private static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

    public int SkyTop(int x, int z)
    {
        int tx = x >> ChunkMath.Shift, tz = z >> ChunkMath.Shift;
        if (_tiles.TryGetValue(Key(tx, tz), out var tile)) return tile[(z & ChunkMath.Mask) * ChunkMath.Size + (x & ChunkMath.Mask)];
        return _roof != null ? _world.SizeCells.Y : _estimate(x, z);
    }

    /// <summary>Lowest y of outdoor shade under a floating roof (equal to <see cref="SkyTop"/> when there is none).</summary>
    public int ShadeTop(int x, int z)
    {
        int tx = x >> ChunkMath.Shift, tz = z >> ChunkMath.Shift;
        if (_tiles.TryGetValue(Key(tx, tz), out var tile)) return tile[ChunkMath.Area + (z & ChunkMath.Mask) * ChunkMath.Size + (x & ChunkMath.Mask)];
        return _estimate(x, z);
    }

    /// <summary>Rescans a chunk column. Returns (column index, old, new) for every column whose value changed.</summary>
    public List<(int X, int Z, int Old, int New)> Rebuild(int chunkX, int chunkZ)
    {
        var changes = new List<(int, int, int, int)>();
        var key = Key(chunkX, chunkZ);
        _tiles.TryGetValue(key, out var old);
        var tile = new int[ChunkMath.Area * 2]; // sky tops, then shade tops
        int ox = chunkX << ChunkMath.Shift, oz = chunkZ << ChunkMath.Shift;
        int topChunk = Math.Max(_columnTopChunk(chunkX, chunkZ), HighestLoaded(chunkX, chunkZ));
        for (int lz = 0; lz < ChunkMath.Size; lz++)
            for (int lx = 0; lx < ChunkMath.Size; lx++)
            {
                int i = lz * ChunkMath.Size + lx;
                if (_roof != null)
                {
                    int cap = _roof(ox + lx, oz + lz);
                    int ground = ScanUnderRoof(ox + lx, oz + lz, chunkX, chunkZ, cap);
                    tile[i] = _world.SizeCells.Y;
                    tile[ChunkMath.Area + i] = ground;
                    int was = old?[ChunkMath.Area + i] ?? _estimate(ox + lx, oz + lz);
                    if (was != ground) changes.Add((ox + lx, oz + lz, Math.Min(was, ground), cap));
                    continue;
                }
                var (sky, shade) = ScanColumn(ox + lx, oz + lz, chunkX, chunkZ, topChunk);
                tile[i] = sky;
                tile[ChunkMath.Area + i] = shade;
                int previous = old?[i] ?? _estimate(ox + lx, oz + lz);
                int previousShade = old?[ChunkMath.Area + i] ?? previous;
                if (previous != sky) changes.Add((ox + lx, oz + lz, previous, sky));
                if (previousShade != shade) changes.Add((ox + lx, oz + lz, Math.Min(previousShade, shade), Math.Max(previous, sky)));
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

    /// <summary>
    /// Don't Dig Up: the shade top of a column — the first blocking cell scanning down from just under the roof (the
    /// ground, or a roof the player built), plus one; the estimate where the chunks below are not loaded.
    /// </summary>
    private int ScanUnderRoof(int x, int z, int cx, int cz, int cap)
    {
        int lx = x & ChunkMath.Mask, lz = z & ChunkMath.Mask;
        int start = Math.Min(cap - 1, _world.SizeCells.Y - 1);
        bool open = false;
        for (int cy = start >> ChunkMath.Shift; cy >= 0; cy--)
        {
            var chunk = _world.GetChunk(new Int3(cx, cy, cz));
            if (chunk == null) return Math.Min(_estimate(x, z), (cy + 1) << ChunkMath.Shift);
            var data = chunk.Capture();
            int fromLy = cy == start >> ChunkMath.Shift ? start & ChunkMath.Mask : ChunkMath.Size - 1;
            for (int ly = fromLy; ly >= 0; ly--)
            {
                int i = ChunkMath.Index(lx, ly, lz);
                bool blocking = _tables.LightFilter[data.GetBlock(i)] != 0 || (data.Liquids != null && data.Liquids[i] != 0) || (data.Shapes != null && data.Shapes[i] != 0);
                if (!open) { open = !blocking; continue; } // the roof's own cells if the estimate starts inside them
                if (blocking) return (cy << ChunkMath.Shift) + ly + 1;
            }
        }
        return 0;
    }

    /// <summary>Sky top and shade top of one column (see the class summary).</summary>
    private (int Sky, int Shade) ScanColumn(int x, int z, int cx, int cz, int topChunk)
    {
        int lx = x & ChunkMath.Mask, lz = z & ChunkMath.Mask;
        int floating = _estimate(x, z) + ShadeMargin;
        int sky = -1, roofBottom = -1;
        for (int cy = topChunk; cy >= 0; cy--)
        {
            var chunk = _world.GetChunk(new Int3(cx, cy, cz));
            if (chunk == null)
            {
                // Unknown content below: fall back to the estimate (never above the known-open part). Under a roof
                // whose ground is not loaded there is no shade until it is.
                if (sky < 0)
                {
                    int estimate = Math.Min(_estimate(x, z), (cy + 1) << ChunkMath.Shift);
                    return (estimate, estimate);
                }
                return (sky, sky);
            }
            var data = chunk.Capture();
            for (int ly = ChunkMath.Size - 1; ly >= 0; ly--)
            {
                int i = ChunkMath.Index(lx, ly, lz);
                int y = (cy << ChunkMath.Shift) + ly;
                bool blocking = _tables.LightFilter[data.GetBlock(i)] != 0 || (data.Liquids != null && data.Liquids[i] != 0) || (data.Shapes != null && data.Shapes[i] != 0);
                if (sky < 0)
                {
                    if (!blocking) continue;
                    sky = y + 1;
                    if (y < floating) return (sky, sky); // the terrain itself: no floating roof
                }
                else if (roofBottom < 0)
                {
                    if (blocking) continue;
                    roofBottom = y + 1; // first air under the roof
                    if (roofBottom <= floating) return (sky, sky);
                }
                else if (blocking)
                {
                    int ground = y + 1;
                    return (sky, roofBottom - ground >= ShadeGap ? ground : sky);
                }
            }
        }
        return sky < 0 ? (0, 0) : (sky, roofBottom >= ShadeGap ? 0 : sky);
    }
}
