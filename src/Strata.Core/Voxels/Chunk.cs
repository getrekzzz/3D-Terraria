using System.Runtime.CompilerServices;
using Strata.Core.Common;

namespace Strata.Core.Voxels;

/// <summary>Lifecycle of a chunk in memory.</summary>
public enum ChunkState
{
    /// <summary>Allocated but voxel data not yet produced.</summary>
    Pending,

    /// <summary>Voxel data available (generated or loaded).</summary>
    Ready,
}

/// <summary>
/// A 32³ block of cells. Layers are stored in <b>copy-on-write</b> arrays: readers (background jobs) grab
/// array references through <see cref="Capture"/> and see an immutable snapshot; writers go through
/// <see cref="EditBatch"/> which clones, modifies and republishes the arrays and bumps <see cref="Version"/>.
/// A null block array means the whole chunk is <see cref="UniformBlock"/> (all air, all stone…).
/// </summary>
public sealed class Chunk
{
    private ChunkData _data;

    public Chunk(Int3 coord, ChunkData data)
    {
        Coord = coord;
        _data = data;
    }

    public Int3 Coord { get; }

    public ChunkState State { get; internal set; } = ChunkState.Ready;

    /// <summary>Loaded from the save rather than generated (its contents may have been shaped by play).</summary>
    public bool FromSave { get; init; }

    /// <summary>Incremented whenever voxel content changes. Job results carry the version they used.</summary>
    public int Version { get; private set; }

    /// <summary>Changed since it was generated/loaded; must be persisted.</summary>
    public bool Modified { get; internal set; }

    /// <summary>Flags gameplay state stored with the chunk (container contents, object state) for saving.</summary>
    public void MarkModified() => Modified = true;

    /// <summary>Number of world mutations (hardmode conversions…) already applied to this chunk.</summary>
    public int MutationLevel { get; internal set; }

    /// <summary>Light computed by the most recent mesh job (sky 4 bits | r | g | b), or null.</summary>
    public ushort[]? Light { get; private set; }

    public int LightVersion { get; private set; } = -1;

    public ChunkData Capture() => Volatile.Read(ref _data);

    public ushort UniformBlock => _data.UniformBlock;

    public bool IsUniform => _data.Blocks == null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetBlock(int index) => _data.GetBlock(index);

    public byte GetShape(int index) => _data.Shapes?[index] ?? 0;

    public byte GetLiquid(int index) => _data.Liquids?[index] ?? 0;

    public CellFlags GetFlags(int index) => (CellFlags)(_data.Flags?[index] ?? 0);

    internal void Publish(ChunkData data)
    {
        Volatile.Write(ref _data, data);
        Version++;
    }

    /// <summary>Stores light produced for <paramref name="version"/> (ignored if stale).</summary>
    public void SetLight(ushort[] light, int version)
    {
        if (version < LightVersion) return;
        Light = light;
        LightVersion = version;
    }
}

/// <summary>Immutable snapshot of a chunk's layers. Arrays must never be mutated once published.</summary>
public sealed class ChunkData
{
    public ChunkData(ushort uniformBlock)
    {
        UniformBlock = uniformBlock;
    }

    public ChunkData(ushort[]? blocks, byte[]? shapes, byte[]? liquids, byte[]? flags, ushort uniformBlock = 0)
    {
        if (blocks != null && blocks.Length != ChunkMath.Volume) throw new ArgumentException("bad block array length");
        Blocks = blocks;
        Shapes = shapes;
        Liquids = liquids;
        Flags = flags;
        UniformBlock = uniformBlock;
    }

    public ushort[]? Blocks { get; }
    public byte[]? Shapes { get; }
    public byte[]? Liquids { get; }
    public byte[]? Flags { get; }
    public ushort UniformBlock { get; }

    public bool IsUniform => Blocks == null;
    public bool HasShapes => Shapes != null;
    public bool HasLiquids => Liquids != null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetBlock(int index) => Blocks == null ? UniformBlock : Blocks[index];

    /// <summary>Collapses uniform arrays to the compact form (used after generation/decoding).</summary>
    public static ChunkData Compact(ushort[] blocks, byte[]? shapes, byte[]? liquids, byte[]? flags)
    {
        shapes = AllZero(shapes) ? null : shapes;
        liquids = AllZero(liquids) ? null : liquids;
        flags = AllZero(flags) ? null : flags;
        ushort first = blocks[0];
        if (shapes == null && liquids == null && flags == null && blocks.AsSpan().IndexOfAnyExcept(first) < 0)
            return new ChunkData(first);
        return new ChunkData(blocks, shapes, liquids, flags);
    }

    private static bool AllZero(byte[]? a) => a == null || a.AsSpan().IndexOfAnyExcept((byte)0) < 0;
}
