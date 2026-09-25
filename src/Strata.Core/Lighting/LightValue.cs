using System.Runtime.CompilerServices;

namespace Strata.Core.Meshing;

/// <summary>Packed 16-bit light: sky (bits 12–15) and block light red/green/blue (4 bits each).</summary>
public static class LightValue
{
    public const int Max = 15;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Pack(int sky, int r, int g, int b) => (ushort)((sky << 12) | (r << 8) | (g << 4) | b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Sky(ushort v) => v >> 12;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int R(ushort v) => (v >> 8) & 0xF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int G(ushort v) => (v >> 4) & 0xF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int B(ushort v) => v & 0xF;

    /// <summary>Brightest of the block channels (used for spawn darkness checks).</summary>
    public static int BlockMax(ushort v) => Math.Max(R(v), Math.Max(G(v), B(v)));
}
