namespace Strata.Core.Determinism;

/// <summary>
/// Derives independent deterministic seeds from a root seed using stable names and coordinates.
/// Streams depend on <em>names</em>, never on call order, so adding a generation pass or feature does
/// not change the output of any other.
/// </summary>
public readonly struct SeedTree
{
    public readonly ulong Value;

    public SeedTree(ulong value) => Value = value;

    public static SeedTree FromWorldSeed(long seed) => new(Hash.Mix(unchecked((ulong)seed) ^ 0x5354524154410000UL));

    public SeedTree Child(string name) => new(Hash.Combine(Value, Hash.String(name)));

    public SeedTree Child(long index) => new(Hash.Combine(Value, index));

    public SeedTree Child(int x, int y, int z) => new(Hash.Cell(Value, x, y, z));

    public Pcg32 Rng() => new(Value);

    public Pcg32 Rng(string name) => Child(name).Rng();

    /// <summary>Stable 32-bit seed for libraries that take an int seed (e.g. FastNoiseLite).</summary>
    public int IntSeed(string name) => unchecked((int)Child(name).Value);

    public override string ToString() => Value.ToString("X16");
}
