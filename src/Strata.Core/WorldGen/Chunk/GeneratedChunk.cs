using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.WorldGen.Chunk;

/// <summary>Output of generating one chunk.</summary>
public sealed class GeneratedChunk
{
    public GeneratedChunk(Int3 coord, ChunkData data, IReadOnlyList<TileObjectInstance> objects, ChunkNavSummary nav)
    {
        Coord = coord;
        Data = data;
        Objects = objects;
        Nav = nav;
    }

    public Int3 Coord { get; }
    public ChunkData Data { get; }
    public IReadOnlyList<TileObjectInstance> Objects { get; }
    public ChunkNavSummary Nav { get; }
}

/// <summary>Navigation/visibility summary produced by the navigation pass.</summary>
public readonly record struct ChunkNavSummary(ushort FaceConnectivity, int WalkableCells, int AirCells)
{
    /// <summary>Bit for "air path exists between face a and face b" (a &lt; b, 15 pairs).</summary>
    public static int PairBit(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        int bit = 0;
        for (int i = 0; i < a; i++) bit += 5 - i;
        return bit + (b - a - 1);
    }

    public bool Connects(int faceA, int faceB) => faceA == faceB || (FaceConnectivity & (1 << PairBit(faceA, faceB))) != 0;
}
