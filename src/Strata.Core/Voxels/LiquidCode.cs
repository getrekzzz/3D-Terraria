namespace Strata.Core.Voxels;

public enum LiquidType : byte
{
    None = 0,
    Water = 1,
    Lava = 2,
    Honey = 3,
    Shimmer = 4,
}

/// <summary>Packs liquid type (bits 5–7) and fill level 0–31 (bits 0–4) into one byte per cell.</summary>
public static class LiquidCode
{
    public const int MaxLevel = 31;

    public static byte Pack(LiquidType type, int level)
    {
        if (type == LiquidType.None || level <= 0) return 0;
        return (byte)(((int)type << 5) | Math.Min(level, MaxLevel));
    }

    public static LiquidType Type(byte code) => (LiquidType)(code >> 5);

    public static int Level(byte code) => code & 0x1F;

    public static bool IsEmpty(byte code) => code == 0;

    public static byte Full(LiquidType type) => Pack(type, MaxLevel);
}

/// <summary>Per-cell flag bits.</summary>
[Flags]
public enum CellFlags : byte
{
    None = 0,

    /// <summary>Placed by a player (blocks natural spawning, affects housing and smart tools).</summary>
    PlayerPlaced = 1,

    /// <summary>Part of a generated structure (dungeon, temple): protected until progression allows.</summary>
    Structure = 2,
}
