using Strata.Core.Common;

namespace Strata.Core.Voxels;

/// <summary>One cell change recorded by an edit batch.</summary>
public readonly record struct CellChange(Int3 Cell, ushort OldBlock, ushort NewBlock, byte OldShape, byte NewShape);

/// <summary>Outcome of a committed <see cref="EditBatch"/>.</summary>
public sealed class EditResult
{
    public List<CellChange> Changes { get; } = new();

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

    public bool SetLiquid(int x, int y, int z, byte liquid)
    {
        var w = GetWorking(x, y, z, out int index);
        if (w == null) return false;
        if ((w.Liquids?[index] ?? 0) == liquid) return true;
        (w.Liquids ??= new byte[ChunkMath.Volume])[index] = liquid;
        w.LiquidChanged = true;
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
        foreach (var coord in _result.ChangedChunks) _result.AffectedChunks.Add(coord);

        if (!_result.IsEmpty) _world.RaiseEdited(_result);
        return _result;
    }

    private void AddInfluence(Int3 cell)
    {
        var min = ChunkMath.CellToChunk(cell - Int3.One * InfluenceRadius);
        var max = ChunkMath.CellToChunk(cell + Int3.One * InfluenceRadius);
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
