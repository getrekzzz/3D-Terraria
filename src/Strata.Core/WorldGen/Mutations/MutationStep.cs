using Strata.Core.Common;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Mutations;

/// <summary>
/// One world mutation, resolved for a world: a pure function of (world, mutation seed, chunk coordinate, chunk
/// blocks), so applying it when a chunk is first generated, when a saved chunk loads, or live to a loaded chunk
/// gives the same result.
/// </summary>
public interface IMutationStep
{
    /// <summary>Could this step change a chunk made entirely of <paramref name="block"/>? (Skips uniform chunks.)</summary>
    bool Affects(ushort block);

    void Apply(ref ChunkBuffer buf, Int3 chunk);
}

/// <summary>A step that turns a region into a zone (biome) for spawns and music.</summary>
public interface IZoneStep
{
    string Zone { get; }

    bool InZone(Int3 cell);
}

/// <summary>A chunk's blocks as a vein/mutation buffer (32³, chunk-local indexing), tracking whether anything changed.</summary>
public struct ChunkBuffer : IBlockBuffer
{
    private readonly ushort[] _blocks;

    public ChunkBuffer(ushort[] blocks, Int3 origin)
    {
        _blocks = blocks;
        Origin = origin;
        Changed = false;
    }

    public Int3 Origin { get; }
    public readonly int Size => ChunkMath.Size;
    public bool Changed { get; private set; }

    public readonly ushort Get(int wx, int wy, int wz) => _blocks[ChunkMath.Index(wx - Origin.X, wy - Origin.Y, wz - Origin.Z)];

    public void Set(int wx, int wy, int wz, ushort block)
    {
        int i = ChunkMath.Index(wx - Origin.X, wy - Origin.Y, wz - Origin.Z);
        if (_blocks[i] == block) return;
        _blocks[i] = block;
        Changed = true;
    }
}

/// <summary>A mutation that does nothing (a saved mutation whose content no longer exists keeps its log slot).</summary>
public sealed class EmptyStep : IMutationStep
{
    public bool Affects(ushort block) => false;

    public void Apply(ref ChunkBuffer buf, Int3 chunk)
    {
    }
}
