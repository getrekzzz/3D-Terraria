namespace Strata.Core.Common;

/// <summary>The six axis directions, in a fixed order used by meshing, lighting and connectivity.</summary>
public static class Faces
{
    public const int Count = 6;
    public const int PosX = 0, NegX = 1, PosY = 2, NegY = 3, PosZ = 4, NegZ = 5;

    public static readonly Int3[] Offsets =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    public static int Opposite(int face) => face ^ 1;

    public static int Axis(int face) => face >> 1;

    public static bool IsPositive(int face) => (face & 1) == 0;
}
