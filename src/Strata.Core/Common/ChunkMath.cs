using System.Runtime.CompilerServices;

namespace Strata.Core.Common;

/// <summary>Conversions between world cells, chunk coordinates and chunk-local indices.</summary>
public static class ChunkMath
{
    public const int Shift = 5;
    public const int Size = 1 << Shift; // 32
    public const int Mask = Size - 1;
    public const int Volume = Size * Size * Size;
    public const int Area = Size * Size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToChunk(int cell) => cell >> Shift; // arithmetic shift floors negatives

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToLocal(int cell) => cell & Mask;

    public static Int3 CellToChunk(Int3 cell) => new(cell.X >> Shift, cell.Y >> Shift, cell.Z >> Shift);
    public static Int3 CellToLocal(Int3 cell) => new(cell.X & Mask, cell.Y & Mask, cell.Z & Mask);
    public static Int3 ChunkOrigin(Int3 chunk) => new(chunk.X << Shift, chunk.Y << Shift, chunk.Z << Shift);

    /// <summary>Linear index inside a chunk. Layout is Y-major then Z then X (x fastest).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(int lx, int ly, int lz) => (ly << (Shift * 2)) | (lz << Shift) | lx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(Int3 local) => Index(local.X, local.Y, local.Z);

    public static Int3 FromIndex(int index) =>
        new(index & Mask, index >> (Shift * 2), (index >> Shift) & Mask);

    public static Aabb3i ChunkBounds(Int3 chunk)
    {
        var o = ChunkOrigin(chunk);
        return new Aabb3i(o, o + Int3.One * Size);
    }
}
