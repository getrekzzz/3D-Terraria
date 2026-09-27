using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Voxels;

/// <summary>
/// The loaded portion of the world: chunks by coordinate, plus tile objects.
/// Chunk lookup is thread-safe; mutation of voxel content goes through <see cref="EditBatch"/> on the main thread.
/// </summary>
public sealed class VoxelWorld
{
    /// <summary>Returned for cells in chunks that are not loaded.</summary>
    public const ushort Unknown = ushort.MaxValue;

    private readonly ConcurrentDictionary<Int3, Chunk> _chunks = new();

    public VoxelWorld(ContentDatabase content, Int3 sizeCells, ushort bedrock)
    {
        Content = content;
        SizeCells = sizeCells;
        Bedrock = bedrock;
        ChunkCounts = new Int3(sizeCells.X >> ChunkMath.Shift, sizeCells.Y >> ChunkMath.Shift, sizeCells.Z >> ChunkMath.Shift);
    }

    public ContentDatabase Content { get; }
    public Int3 SizeCells { get; }
    public Int3 ChunkCounts { get; }

    /// <summary>Block reported below the world floor.</summary>
    public ushort Bedrock { get; }

    public TileObjectStore TileObjects { get; } = new();

    public int ChunkCount => _chunks.Count;
    public ICollection<Chunk> Chunks => _chunks.Values;

    /// <summary>Raised on the main thread after an edit batch commits.</summary>
    public event Action<EditResult>? Edited;

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)SizeCells.X && (uint)y < (uint)SizeCells.Y && (uint)z < (uint)SizeCells.Z;

    public bool ChunkInBounds(Int3 c) =>
        (uint)c.X < (uint)ChunkCounts.X && (uint)c.Y < (uint)ChunkCounts.Y && (uint)c.Z < (uint)ChunkCounts.Z;

    public Chunk? GetChunk(Int3 coord) => _chunks.TryGetValue(coord, out var c) ? c : null;

    public bool HasChunk(Int3 coord) => _chunks.ContainsKey(coord);

    public void AddChunk(Chunk chunk) => _chunks[chunk.Coord] = chunk;

    public bool RemoveChunk(Int3 coord, out Chunk? chunk)
    {
        var ok = _chunks.TryRemove(coord, out var c);
        chunk = c;
        return ok;
    }

    /// <summary>Block at a cell; <see cref="Unknown"/> if its chunk is not loaded. Outside the world: bedrock below, air above/around.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetBlock(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return y < 0 ? Bedrock : ContentDatabase.Air;
        var chunk = GetChunk(new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift));
        return chunk == null ? Unknown : chunk.GetBlock(ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask));
    }

    public ushort GetBlock(Int3 c) => GetBlock(c.X, c.Y, c.Z);

    public byte GetShape(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return 0;
        var chunk = GetChunk(new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift));
        return chunk?.GetShape(ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)) ?? 0;
    }

    public byte GetLiquid(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return 0;
        var chunk = GetChunk(new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift));
        return chunk?.GetLiquid(ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)) ?? 0;
    }

    public CellFlags GetFlags(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return CellFlags.None;
        var chunk = GetChunk(new Int3(x >> ChunkMath.Shift, y >> ChunkMath.Shift, z >> ChunkMath.Shift));
        return chunk?.GetFlags(ChunkMath.Index(x & ChunkMath.Mask, y & ChunkMath.Mask, z & ChunkMath.Mask)) ?? CellFlags.None;
    }

    /// <summary>Is the cell's block solid for collision (unknown chunks count as solid to be safe)?</summary>
    public bool IsSolid(int x, int y, int z)
    {
        var b = GetBlock(x, y, z);
        return b == Unknown || Content.Blocks[b].IsSolid;
    }

    public EditBatch BeginEdit() => new(this);

    internal void RaiseEdited(EditResult result) => Edited?.Invoke(result);
}
