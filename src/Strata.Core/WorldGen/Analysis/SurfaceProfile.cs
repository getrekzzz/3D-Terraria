using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Analysis;

/// <summary>Per-column findings of a <see cref="SurfaceProfile"/> scan.</summary>
[Flags]
public enum SurfaceFlags : byte
{
    None = 0,

    /// <summary>The topmost solid run is at most <see cref="SurfaceProfile.LipMaxThickness"/> cells thick with open space under it (overhang, thin roof).</summary>
    Lip = 1,

    /// <summary>At least one solid cell within <see cref="SurfaceProfile.FloatingDepth"/> of Top has two or more open cells under it.</summary>
    Floating = 2,

    /// <summary>Top lies more than <see cref="SurfaceProfile.BreachDepth"/> cells below the terrain surface height (trench, cave breaking through).</summary>
    Breach = 4,

    /// <summary>Liquid sits directly above Top.</summary>
    Water = 8,
}

/// <summary>
/// Top-down analysis of a rectangular window of generated terrain: the highest solid cell ("Top") of every column
/// plus lip/floating/breach/water flags. The before/after evidence for terrain changes (<see cref="Stats"/>,
/// worldgen-report). The metric definitions (constants below) must stay fixed so older reports remain comparable.
/// </summary>
public sealed partial class SurfaceProfile
{
    /// <summary>Scan starts this many cells above the column's surface height (covers the overhang amplitude plus margin).</summary>
    public const int Headroom = 24;

    /// <summary>Scan ends this many cells below the column's surface height; a column open all the way down is a breach.</summary>
    public const int Footroom = 24;

    public const int LipMaxThickness = 6;
    public const int FloatingDepth = 12;
    public const int FloatingGap = 2;
    public const float BreachDepth = 3f;

    private const sbyte Missing = -1, Open = 0, Solid = 1, Liquid = 2;

    private SurfaceProfile(ContentDatabase content, int x0, int z0, int width, int depth)
    {
        Content = content;
        X0 = x0;
        Z0 = z0;
        Width = width;
        Depth = depth;
        int n = width * depth;
        Top = new int[n];
        Surface = new float[n];
        Flags = new SurfaceFlags[n];
        FloatingCells = new byte[n];
    }

    public ContentDatabase Content { get; }
    public int X0 { get; }
    public int Z0 { get; }
    public int Width { get; }
    public int Depth { get; }
    public int ColumnCount => Width * Depth;

    /// <summary>Highest solid cell per column (row-major from <see cref="X0"/>, <see cref="Z0"/>); one below the scan band if none.</summary>
    public int[] Top { get; }

    /// <summary>Terrain surface height per column (the reference for breaches and the scan band).</summary>
    public float[] Surface { get; }

    public SurfaceFlags[] Flags { get; }

    /// <summary>Solid cells with open space under them within <see cref="FloatingDepth"/> of Top, per column.</summary>
    public byte[] FloatingCells { get; }

    /// <summary>Surface biome runtime id per column (set by <see cref="Measure"/>; null for synthetic profiles).</summary>
    public byte[]? Biome { get; private set; }

    /// <summary>Chunks generated to build this profile (0 for <see cref="FromChunks"/>).</summary>
    public int ChunksGenerated { get; private set; }

    public bool Contains(int x, int z) => (uint)(x - X0) < (uint)Width && (uint)(z - Z0) < (uint)Depth;

    public int Index(int x, int z) => (z - Z0) * Width + (x - X0);

    public int TopAt(int x, int z) => Top[Index(x, z)];

    public SurfaceFlags FlagsAt(int x, int z) => Flags[Index(x, z)];

    /// <summary>Aggregated metrics over the columns <paramref name="include"/> accepts (world x, z), or all columns.</summary>
    public TerrainStats Stats(Func<int, int, bool>? include = null) => TerrainStats.Compute(this, include);

    /// <summary>
    /// Profiles a window from already-generated chunk data. <paramref name="chunkAt"/> returns null for chunks that were
    /// not produced; a column's scan ends at the first missing chunk, so callers supply every chunk from
    /// <paramref name="yMax"/> down to the surface (air above it is fine) and as far below as they want examined.
    /// Each column is scanned from surface + <see cref="Headroom"/> down to surface − <see cref="Footroom"/>, clamped to
    /// [<paramref name="yMin"/>, <paramref name="yMax"/>].
    /// </summary>
    public static SurfaceProfile FromChunks(ContentDatabase db, Func<Int3, ChunkData?> chunkAt, int x0, int z0, int width, int depth,
        int yMin, int yMax, Func<float, float, float> surfaceHeight)
    {
        if (width <= 0 || depth <= 0) throw new ArgumentException("empty window");
        var profile = new SurfaceProfile(db, x0, z0, width, depth);
        var solid = SolidTable(db);
        int cx0 = ChunkMath.ToChunk(x0), cx1 = ChunkMath.ToChunk(x0 + width - 1);
        int cz0 = ChunkMath.ToChunk(z0), cz1 = ChunkMath.ToChunk(z0 + depth - 1);
        int cyMin = ChunkMath.ToChunk(yMin), cyMax = ChunkMath.ToChunk(yMax);
        var column = new ChunkData?[cyMax - cyMin + 1];
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cy = cyMin; cy <= cyMax; cy++) column[cy - cyMin] = chunkAt(new Int3(cx, cy, cz));
                profile.ScanChunkColumn(cx, cz, new ColumnReader(column, cyMin, solid), yMin, yMax, surfaceHeight);
            }
        return profile;
    }

    /// <summary>Scans the window columns inside chunk column (cx, cz). Writes only those columns (safe to run in parallel).</summary>
    private void ScanChunkColumn(int cx, int cz, ColumnReader reader, int yMin, int yMax, Func<float, float, float> surfaceHeight)
    {
        int xa = Math.Max(X0, cx << ChunkMath.Shift), xb = Math.Min(X0 + Width, (cx + 1) << ChunkMath.Shift);
        int za = Math.Max(Z0, cz << ChunkMath.Shift), zb = Math.Min(Z0 + Depth, (cz + 1) << ChunkMath.Shift);
        for (int z = za; z < zb; z++)
            for (int x = xa; x < xb; x++)
            {
                int i = Index(x, z);
                float s = surfaceHeight(x, z);
                Surface[i] = s;
                int top = (int)MathF.Floor(s) + Headroom, bottom = (int)MathF.Floor(s) - Footroom;
                reader.Local(x, z);
                ScanColumn(i, reader, Math.Max(yMin, bottom), Math.Min(yMax, top));
            }
    }

    private void ScanColumn(int i, ColumnReader r, int bottom, int top)
    {
        int y = top, lowest = top + 1;
        bool found = false;
        while (y >= bottom)
        {
            var data = r.ChunkAt(y);
            if (data == null) break;
            int chunkBottom = Math.Max(bottom, y & ~ChunkMath.Mask);
            if (data.IsUniform && !r.IsSolidBlock(data.UniformBlock))
            {
                lowest = chunkBottom;
                y = chunkBottom - 1;
                continue;
            }
            for (; y >= chunkBottom; y--)
            {
                lowest = y;
                if (r.State(data, y) == Solid) { found = true; break; }
            }
            if (found) break;
        }

        if (!found)
        {
            Top[i] = lowest - 1;
            Flags[i] = SurfaceFlags.Breach;
            return;
        }

        Top[i] = y;
        var flags = SurfaceFlags.None;
        if (y < top && r.StateAt(y + 1) == Liquid) flags |= SurfaceFlags.Water;
        if (Surface[i] - y > BreachDepth) flags |= SurfaceFlags.Breach;

        int run = 1;
        while (run <= LipMaxThickness && y - run >= bottom && r.StateAt(y - run) == Solid) run++;
        if (run <= LipMaxThickness && y - run >= bottom && IsOpen(r.StateAt(y - run))) flags |= SurfaceFlags.Lip;

        int floating = 0;
        for (int cy = y; cy >= y - FloatingDepth && cy - FloatingGap >= bottom; cy--)
        {
            if (r.StateAt(cy) != Solid) continue;
            bool gap = true;
            for (int g = 1; g <= FloatingGap && gap; g++) gap = IsOpen(r.StateAt(cy - g));
            if (gap) floating++;
        }
        if (floating > 0) flags |= SurfaceFlags.Floating;
        FloatingCells[i] = (byte)floating;
        Flags[i] = flags;
    }

    private static bool IsOpen(sbyte state) => state is Open or Liquid;

    private static bool[] SolidTable(ContentDatabase db)
    {
        var table = new bool[db.Blocks.Count];
        for (int b = 0; b < table.Length; b++) table[b] = db.Blocks[b].IsSolid;
        return table;
    }

    /// <summary>Cell states of one column across a stack of chunks (reused for every column of a chunk column).</summary>
    private struct ColumnReader
    {
        private readonly ChunkData?[] _chunks;
        private readonly int _cyMin;
        private readonly bool[] _solid;
        private int _lx, _lz;

        public ColumnReader(ChunkData?[] chunks, int cyMin, bool[] solid)
        {
            _chunks = chunks;
            _cyMin = cyMin;
            _solid = solid;
        }

        public void Local(int x, int z)
        {
            _lx = ChunkMath.ToLocal(x);
            _lz = ChunkMath.ToLocal(z);
        }

        public readonly ChunkData? ChunkAt(int y)
        {
            int k = ChunkMath.ToChunk(y) - _cyMin;
            return (uint)k < (uint)_chunks.Length ? _chunks[k] : null;
        }

        public readonly bool IsSolidBlock(ushort block) => block < _solid.Length && _solid[block];

        public readonly sbyte StateAt(int y)
        {
            var data = ChunkAt(y);
            return data == null ? Missing : State(data, y);
        }

        public readonly sbyte State(ChunkData data, int y)
        {
            int idx = ChunkMath.Index(_lx, ChunkMath.ToLocal(y), _lz);
            if (IsSolidBlock(data.GetBlock(idx))) return Solid;
            return (data.Liquids?[idx] ?? 0) != 0 ? Liquid : Open;
        }
    }
}
