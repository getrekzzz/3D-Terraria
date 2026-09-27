using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Meshing;

/// <summary>
/// A 64³ copy of the cells around one chunk (the chunk ± 16 cells), assembled from immutable chunk
/// snapshots of its 26 neighbours. Light propagates at most 15 cells, so light and meshing for the
/// centre chunk computed from this region are exact. Buffers are pooled per thread.
/// </summary>
public sealed class Neighborhood
{
    public const int Margin = 16;
    public const int Size = ChunkMath.Size + 2 * Margin; // 64
    public const int Volume = Size * Size * Size;
    public const int Layer = Size * Size;

    [ThreadStatic] private static Neighborhood? _pooled;

    private Neighborhood()
    {
    }

    public Int3 Chunk { get; private set; }

    /// <summary>World cell at region index (0,0,0).</summary>
    public Int3 Origin { get; private set; }

    public ushort[] Blocks { get; } = new ushort[Volume];
    public byte[] Shapes { get; } = new byte[Volume];
    public byte[] Liquids { get; } = new byte[Volume];
    public byte[] Flags { get; } = new byte[Volume];
    public bool HasFlags { get; private set; }
    public bool HasShapes { get; private set; }
    public bool HasLiquids { get; private set; }

    /// <summary>Versions of the 27 chunks captured (centre at index 13), for staleness checks.</summary>
    public int[] Versions { get; } = new int[27];

    public static int Index(int x, int y, int z) => (y * Size + z) * Size + x;

    /// <summary>Captures the neighbourhood of <paramref name="chunk"/>. Returns false if a needed neighbour is missing.</summary>
    public static Neighborhood? Capture(VoxelWorld world, Int3 chunk)
    {
        var nb = _pooled ??= new Neighborhood();
        nb.Chunk = chunk;
        nb.Origin = ChunkMath.ChunkOrigin(chunk) - Int3.One * Margin;
        nb.HasShapes = false;
        nb.HasLiquids = false;
        nb.HasFlags = false;
        Array.Clear(nb.Shapes);
        Array.Clear(nb.Liquids);
        Array.Clear(nb.Flags);
        int v = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var c = chunk + new Int3(dx, dy, dz);
                    ChunkData? data;
                    if (world.ChunkInBounds(c))
                    {
                        var ch = world.GetChunk(c);
                        if (ch == null || ch.State != ChunkState.Ready) return null;
                        data = ch.Capture();
                        nb.Versions[v] = ch.Version;
                    }
                    else
                    {
                        data = new ChunkData(c.Y < 0 ? world.Bedrock : (ushort)0);
                        nb.Versions[v] = 0;
                    }
                    nb.CopySlab(data, dx, dy, dz);
                    v++;
                }
        return nb;
    }

    /// <summary>Takes the pooled instance out of the thread cache so the caller may keep it.</summary>
    public void Detach()
    {
        if (ReferenceEquals(_pooled, this)) _pooled = null;
    }

    private void CopySlab(ChunkData data, int dx, int dy, int dz)
    {
        // Region range covered by this neighbour along each axis.
        Range(dx, out int rx0, out int rx1, out int sx0);
        Range(dy, out int ry0, out int ry1, out int sy0);
        Range(dz, out int rz0, out int rz1, out int sz0);
        int width = rx1 - rx0;
        for (int ry = ry0; ry < ry1; ry++)
            for (int rz = rz0; rz < rz1; rz++)
            {
                int dst = Index(rx0, ry, rz);
                int src = ChunkMath.Index(sx0, sy0 + ry - ry0, sz0 + rz - rz0);
                if (data.Blocks == null) Blocks.AsSpan(dst, width).Fill(data.UniformBlock);
                else Array.Copy(data.Blocks, src, Blocks, dst, width);
                if (data.Shapes != null)
                {
                    Array.Copy(data.Shapes, src, Shapes, dst, width);
                    HasShapes = true;
                }
                if (data.Liquids != null)
                {
                    Array.Copy(data.Liquids, src, Liquids, dst, width);
                    HasLiquids = true;
                }
                if (data.Flags != null)
                {
                    Array.Copy(data.Flags, src, Flags, dst, width);
                    HasFlags = true;
                }
            }
    }

    private static void Range(int d, out int r0, out int r1, out int s0)
    {
        switch (d)
        {
            case -1: r0 = 0; r1 = Margin; s0 = ChunkMath.Size - Margin; break;
            case 0: r0 = Margin; r1 = Margin + ChunkMath.Size; s0 = 0; break;
            default: r0 = Margin + ChunkMath.Size; r1 = Size; s0 = 0; break;
        }
    }
}
