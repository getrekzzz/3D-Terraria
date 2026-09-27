namespace Strata.Core.Voxels;

/// <summary>Geometric shape of a crisp (built) cell. Natural smooth cells are always <see cref="Full"/>.</summary>
public enum CellShape : byte
{
    Full = 0,
    HalfBottom = 1,
    HalfTop = 2,

    /// <summary>Ramp rising towards the rotation direction.</summary>
    Slope = 3,

    /// <summary>Ramp hanging from the ceiling.</summary>
    SlopeCeiling = 4,
    Stair = 5,
    StairCeiling = 6,

    /// <summary>Thin vertical wall on the rotation side of the cell (windows, fences, thin walls).</summary>
    Panel = 7,

    /// <summary>Thin one-way floor at the top of the cell (drop through / jump up).</summary>
    Platform = 8,

    /// <summary>Thin vertical post in the centre of the cell.</summary>
    Post = 9,
}

/// <summary>Packs shape (low 4 bits) and rotation (bits 4–5, quarter turns about +Y).</summary>
public static class ShapeCode
{
    public const byte FullCode = 0;
    public const int ShapeCount = 10;

    public static byte Pack(CellShape shape, int rotation) => (byte)(((int)shape & 0xF) | ((rotation & 3) << 4));

    public static CellShape Shape(byte code) => (CellShape)(code & 0xF);

    public static int Rotation(byte code) => (code >> 4) & 3;

    public static bool IsFull(byte code) => (code & 0xF) == 0;

    /// <summary>Shapes whose solid part supports standing on top of the full cell height.</summary>
    public static bool IsWalkableTop(CellShape s) => s is CellShape.Full or CellShape.HalfTop or CellShape.Platform or CellShape.StairCeiling or CellShape.SlopeCeiling;
}
