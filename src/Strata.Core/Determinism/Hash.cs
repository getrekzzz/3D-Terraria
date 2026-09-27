using System.Runtime.CompilerServices;

namespace Strata.Core.Determinism;

/// <summary>
/// Stable, platform-independent 64-bit hashing used to derive all procedural randomness.
/// Never use <see cref="string.GetHashCode()"/> for anything persistent — it is randomized per process.
/// </summary>
public static class Hash
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>SplitMix64 finalizer: a strong bijective 64-bit mixer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Combine(ulong a, ulong b) => Mix(a ^ Mix(b + 0x632BE59BD9B4E019UL));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Combine(ulong a, long b) => Combine(a, unchecked((ulong)b));

    /// <summary>Hash of a string (UTF-16 code units, FNV-1a then mixed). Stable forever.</summary>
    public static ulong String(string s)
    {
        ulong h = FnvOffset;
        foreach (char c in s)
        {
            h ^= (byte)c;
            h *= FnvPrime;
            h ^= (byte)(c >> 8);
            h *= FnvPrime;
        }
        return Mix(h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Cell(ulong seed, int x, int y, int z)
    {
        ulong h = Combine(seed, x);
        h = Combine(h, y);
        return Combine(h, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Cell(ulong seed, int x, int z) => Combine(Combine(seed, x), z);

    /// <summary>Uniform float in [0,1) from a hash (24 bits of precision).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToUnitFloat(ulong h) => (h >> 40) * (1f / (1 << 24));

    /// <summary>Uniform double in [0,1) from a hash (53 bits of precision).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToUnitDouble(ulong h) => (h >> 11) * (1.0 / (1UL << 53));

    /// <summary>Bias-free-enough integer in [0, n) for small n.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToRange(ulong h, int n) => (int)(((h >> 32) * (ulong)n) >> 32);
}
