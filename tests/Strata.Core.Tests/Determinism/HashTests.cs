using Strata.Core.Determinism;

namespace Strata.Core.Tests.Determinism;

public class HashTests
{
    // Golden values: these must never change, or every existing world would regenerate differently.
    [Fact]
    public void StringHashIsStable()
    {
        Assert.Equal(0x0560C5A02720F8B7UL, Hash.String("terrain"));
        Assert.Equal(Hash.String("caves"), Hash.String("caves"));
        Assert.NotEqual(Hash.String("caves"), Hash.String("cavez"));
    }

    [Fact]
    public void CellHashIsStableAndSensitiveToEachAxis()
    {
        ulong a = Hash.Cell(42, 1, 2, 3);
        Assert.Equal(a, Hash.Cell(42, 1, 2, 3));
        Assert.NotEqual(a, Hash.Cell(42, 2, 1, 3));
        Assert.NotEqual(a, Hash.Cell(42, 1, 3, 2));
        Assert.NotEqual(a, Hash.Cell(43, 1, 2, 3));
    }

    [Fact]
    public void UnitFloatIsInRange()
    {
        for (ulong i = 0; i < 10_000; i++)
        {
            float f = Hash.ToUnitFloat(Hash.Mix(i));
            Assert.InRange(f, 0f, 0.99999994f);
        }
    }

    [Fact]
    public void Pcg32SequenceIsStable()
    {
        var rng = new Pcg32(12345);
        uint[] first = { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };
        var again = new Pcg32(12345);
        Assert.Equal(first, new[] { again.NextUInt(), again.NextUInt(), again.NextUInt() });
    }

    [Fact]
    public void Pcg32RangeIsUniformEnough()
    {
        var rng = new Pcg32(7);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++) counts[rng.NextInt(10)]++;
        foreach (var c in counts) Assert.InRange(c, 9_500, 10_500);
    }

    [Fact]
    public void SeedTreeChildrenDependOnNamesNotOrder()
    {
        var root = SeedTree.FromWorldSeed(99);
        var a1 = root.Child("ores").Value;
        _ = root.Child("caves");
        var a2 = root.Child("ores").Value;
        Assert.Equal(a1, a2);
        Assert.NotEqual(root.Child("ores").Value, root.Child("caves").Value);
    }
}
