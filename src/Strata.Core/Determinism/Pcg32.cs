using System.Runtime.CompilerServices;

namespace Strata.Core.Determinism;

/// <summary>PCG-XSH-RR 32-bit generator. Small, fast, deterministic across platforms.</summary>
public struct Pcg32
{
    private ulong _state;
    private readonly ulong _inc;

    public Pcg32(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
    {
        _state = 0;
        _inc = (stream << 1) | 1UL;
        NextUInt();
        _state += Hash.Mix(seed);
        NextUInt();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * 6364136223846793005UL + _inc);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>Integer in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);
    }

    /// <summary>Integer in [min, maxInclusive].</summary>
    public int Range(int min, int maxInclusive) => min + NextInt(maxInclusive - min + 1);

    /// <summary>Float in [0,1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / (1 << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>Signed float in [-1,1).</summary>
    public float NextSigned() => NextFloat() * 2f - 1f;

    public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
