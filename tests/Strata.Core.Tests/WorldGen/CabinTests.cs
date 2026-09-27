using Strata.Core.Tests.Support;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.Tests.WorldGen;

/// <summary>Underground cabins are found by exploring: they stand in the cave tunnels, and deep ones hold Gold Chests.</summary>
public class CabinTests
{
    private static readonly Lazy<WorldGenerator> Small = new(() => new WorldGenerator(TestPaths.Content, new WorldSettings { Seed = 1234, Size = WorldSizePreset.Small }));

    private static List<StructureFeature> Cabins() => Small.Value.Plan.Features.OfKind<StructureFeature>("cabin").ToList();

    [Fact]
    public void MostCabinsStandInACaveTunnel()
    {
        var cabins = Cabins();
        Assert.True(cabins.Count >= 10, $"only {cabins.Count} cabins");
        var tunnels = Small.Value.Plan.Features.OfKind<CapsuleFeature>("worm").ToList();
        int inTunnel = cabins.Count(c => tunnels.Any(t => c.Bounds.Intersects(t.Bounds)));
        Assert.True(inTunnel * 3 >= cabins.Count * 2, $"{inTunnel} of {cabins.Count} cabins touch a tunnel");
    }

    [Fact]
    public void DeepCabinsHoldGoldChestsAndShallowOnesWoodenChests()
    {
        var bands = Small.Value.Plan.Bands;
        int deep = 0, shallow = 0;
        foreach (var cabin in Cabins())
        {
            var chests = cabin.Blueprint.Ops.OfType<ObjectOp>().Where(o => o.Loot is "gold_chest" or "cabin_chest").ToList();
            var main = Assert.Single(chests);
            if (bands.LayerAt(main.Anchor.Y) is WorldLayer.Cavern or WorldLayer.Underworld)
            {
                Assert.Equal("gold_chest", main.ObjectId);
                Assert.Equal("gold_chest", main.Loot);
                deep++;
            }
            else
            {
                Assert.Equal("wooden_chest", main.ObjectId);
                shallow++;
            }
            Assert.Contains(cabin.Blueprint.Ops.OfType<ObjectOp>(), o => o.ObjectId == "torch");
        }
        Assert.True(deep > 0 && shallow > 0, $"deep {deep}, shallow {shallow}");
    }

    [Fact]
    public void TunnelCabinsOpenOntoTheTunnelAtBothEnds()
    {
        foreach (var cabin in Cabins())
        {
            // Passages carved beyond the walls reach the rock outside the cabin's box on two opposite sides.
            var outside = cabin.Blueprint.Ops.OfType<CarveOp>().Where(c => !cabin.Blueprint.Ops.OfType<FillOp>().First().Bounds.Contains(c.Bounds.Min)
                || !cabin.Blueprint.Ops.OfType<FillOp>().First().Bounds.Contains(c.Bounds.Max - Common.Int3.One)).ToList();
            Assert.True(outside.Count is 0 or >= 2, $"cabin at {cabin.Bounds.Min} has {outside.Count} passages out");
        }
        Assert.Contains(Cabins(), c => c.Blueprint.Ops.OfType<CarveOp>().Count(op => !c.Blueprint.Ops.OfType<FillOp>().First().Bounds.Contains(op.Bounds.Min)) >= 1);
    }
}
