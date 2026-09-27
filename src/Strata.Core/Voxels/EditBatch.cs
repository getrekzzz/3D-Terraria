using Strata.Core.Common;

namespace Strata.Core.Voxels;

/// <summary>One cell change recorded by an edit batch.</summary>
public readonly record struct CellChange(Int3 Cell, ushort OldBlock, ushort NewBlock, byte OldShape, byte NewShape);

/// <summary>One liquid change recorded by an edit batch (liquid bytes, see <see cref="LiquidCode"/>).</summary>
public readonly record struct LiquidChange(Int3 Cell, byte Old, byte New);

/// <summary>Outcome of a committed <see cref="EditBatch"/>.</summary>
public sealed class EditResult
{
    public List<CellChange> Changes { get; } = new();

    /// <summary>Liquid writes (type or level), in order.</summary>
    public List<LiquidChange> LiquidChanges { get; } = new();

    /// <summary>Chunks whose voxel data changed.</summary>
    public HashSet<Int3> ChangedChunks { get; } = new();

    /// <summary>Chunks whose mesh/light may be affected (changed chunks + neighbours within light radius).</summary>
    public HashSet<Int3> AffectedChunks { get; } = new();

    public bool IsEmpty => Changes.Count == 0 && ChangedChunks.Count == 0;
}

/// <summary>
/// Groups voxel writes and publishes them atomically per chunk (copy-on-write). Main thread only.
/// Writes into chunks that are not loaded are rejected.
/// </summary>
public sealed class EditBatch
{
    /// <summary>Light propagates this many cells; edits affect meshes within this radius.</summary>
    public const int InfluenceRadius = 16;

    private readonly VoxelWorld _world;
    private readonly Dictionary<Int3, Working> _working = new();
    private readonly EditResult _result = new();
    private bool _committed;

    internal EditBatch(VoxelWorld world) => _world = world;

    public int PendingChanges => _result.Changes.Count;

    /// <summary>Reads a block including this batch's pending changes.</summary>
    public ushort GetBlock(int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z)) return _world.GetBlock(x, y, z);
        var coord = new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift);
        return _working.TryGetValue(coord, out var w)
            ? w.Blocks[ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)]
            : _world.GetBlock(x, y, z);
    }

    /// <summary>Reads a shape code including this batch's pending changes.</summary>
    public byte GetShape(int x, int y, int z)
    {
        var coord = new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift);
        return _working.TryGetValue(coord, out var w)
            ? w.Shapes?[ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)] ?? 0
            : _world.GetShape(x, y, z);
    }

    /// <summary>Reads a liquid byte including this batch's pending changes.</summary>
    public byte GetLiquid(int x, int y, int z)
    {
        if (!_world.InBounds(x, y, z)) return 0;
        var coord = new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift);
        return _working.TryGetValue(coord, out var w)
            ? w.Liquids?[ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)] ?? 0
            : _world.GetLiquid(x, y, z);
    }

    public byte GetLiquid(Int3 c) => GetLiquid(c.X, c.Y, c.Z);

    public ushort GetBlock(Int3 c) => GetBlock(c.X, c.Y, c.Z);

    public bool SetLiquid(Int3 c, byte liquid) => SetLiquid(c.X, c.Y, c.Z, liquid);

    public bool SetBlock(Int3 cell, ushort block, byte shape = 0, CellFlags setFlags = CellFlags.None) =>
        SetBlock(cell.X, cell.Y, cell.Z, block, shape, setFlags);

    public bool SetBlock(int x, int y, int z, ushort block, byte shape = 0, CellFlags setFlags = CellFlags.None)
    {
        var w = GetWorking(x, y, z, out int index);
        if (w == null) return false;
        ushort old = w.Blocks[index];
        byte oldShape = w.Shapes?[index] ?? 0;
        if (old == block && oldShape == shape && setFlags == CellFlags.None) return true;

        w.Blocks[index] = block;
        if (shape != 0 || w.Shapes != null) (w.Shapes ??= new byte[ChunkMath.Volume])[index] = shape;
        if (block == 0)
        {
            if (w.Flags != null) w.Flags[index] = 0;
        }
        else if (setFlags != CellFlags.None)
        {
            (w.Flags ??= new byte[ChunkMath.Volume])[index] |= (byte)setFlags;
        }
        _result.Changes.Add(new CellChange(new Int3(x, y, z), old, block, oldShape, shape));
        return true;
    }

    /// <summary>Paints (or, with 0, scrapes) the block in a cell. Returns false for empty or unloaded cells.</summary>
    public bool SetPaint(Int3 cell, int paint)
    {
        var w = GetWorking(cell.X, cell.Y, cell.Z, out int index);
        if (w == null || w.Blocks[index] == 0) return false;
        byte old = w.Flags?[index] ?? 0;
        byte now = CellPaint.With(old, paint);
        if (old == now) return true;
        (w.Flags ??= new byte[ChunkMath.Volume])[index] = now;
        ushort block = w.Blocks[index];
        byte shape = w.Shapes?[index] ?? 0;
        _result.Changes.Add(new CellChange(cell, block, block, shape, shape));
        return true;
    }

    public bool SetLiquid(int x, int y, int z, byte liquid)
    {
        var w = GetWorking(x, y, z, out int index);
        if (w == null) return false;
        byte old = w.Liquids?[index] ?? 0;
        if (old == liquid) return true;
        (w.Liquids ??= new byte[ChunkMath.Volume])[index] = liquid;
        w.LiquidChanged = true;
        _result.LiquidChanges.Add(new LiquidChange(new Int3(x, y, z), old, liquid));
        return true;
    }

    /// <summary>Publishes all changes. Returns what changed and which chunks need remeshing.</summary>
    public EditResult Commit()
    {
        if (_committed) throw new InvalidOperationException("EditBatch already committed.");
        _committed = true;

        foreach (var (coord, w) in _working)
        {
            w.Chunk.Publish(ChunkData.Compact(w.Blocks, w.Shapes, w.Liquids, w.Flags));
            w.Chunk.Modified = true;
            _result.ChangedChunks.Add(coord);
        }

        foreach (var change in _result.Changes) AddInfluence(change.Cell);
        foreach (var change in _result.LiquidChanges) AddLiquidInfluence(change);
        foreach (var coord in _result.ChangedChunks) _result.AffectedChunks.Add(coord);

        if (!_result.IsEmpty) _world.RaiseEdited(_result);
        return _result;
    }

    private void AddInfluence(Int3 cell) => AddBox(cell - Int3.One * InfluenceRadius, cell + Int3.One * InfluenceRadius);

    /// <summary>
    /// Liquid surfaces blend with neighbouring columns, so border cells touch the next chunk's mesh; glowing liquids
    /// (lava) relight their whole light radius.
    /// </summary>
    private void AddLiquidInfluence(LiquidChange change)
    {
        var content = _world.Content;
        if (content.LiquidOf(change.Old)?.Light != null || content.LiquidOf(change.New)?.Light != null)
        {
            AddInfluence(change.Cell);
            return;
        }
        const int last = ChunkMath.Size - 1;
        var c = change.Cell;
        int lx = c.X & ChunkMath.Mask, ly = c.Y & ChunkMath.Mask, lz = c.Z & ChunkMath.Mask;
        if (lx is > 0 and < last && ly is > 0 and < last && lz is > 0 and < last) return;
        AddBox(c - Int3.One, c + Int3.One);
    }

    private void AddBox(Int3 minCell, Int3 maxCell)
    {
        var min = ChunkMath.CellToChunk(minCell);
        var max = ChunkMath.CellToChunk(maxCell);
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var c = new Int3(x, y, z);
                    if (_world.HasChunk(c)) _result.AffectedChunks.Add(c);
                }
    }

    private Working? GetWorking(int x, int y, int z, out int index)
    {
        index = 0;
        if (_committed) throw new InvalidOperationException("EditBatch already committed.");
        if (!_world.InBounds(x, y, z)) return null;
        var coord = new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift);
        if (!_working.TryGetValue(coord, out var w))
        {
            var chunk = _world.GetChunk(coord);
            if (chunk == null || chunk.State != ChunkState.Ready) return null;
            w = new Working(chunk, chunk.Capture());
            _working[coord] = w;
        }
        index = ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask);
        return w;
    }

    private sealed class Working
    {
        public Working(Chunk chunk, ChunkData data)
        {
            Chunk = chunk;
            if (data.Blocks != null)
            {
                Blocks = (ushort[])data.Blocks.Clone();
            }
            else
            {
                Blocks = new ushort[ChunkMath.Volume];
                Array.Fill(Blocks, data.UniformBlock);
            }
            Shapes = (byte[]?)data.Shapes?.Clone();
            Liquids = (byte[]?)data.Liquids?.Clone();
            Flags = (byte[]?)data.Flags?.Clone();
        }

        public Chunk Chunk { get; }
        public ushort[] Blocks { get; }
        public byte[]? Shapes { get; set; }
        public byte[]? Liquids { get; set; }
        public byte[]? Flags { get; set; }
        public bool LiquidChanged { get; set; }
    }
}
