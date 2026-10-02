using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay.Fishing;

public class CrateLootTests
{
    private static readonly string[] BiomeCrates =
        { "jungle_crate", "sky_crate", "corrupt_crate", "crimson_crate", "hallowed_crate", "dungeon_crate", "frozen_crate", "oasis_crate", "ocean_crate", "obsidian_crate" };

    [Fact]
    public void EveryCrateOpensIntoLoot()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(11);
        foreach (var id in BiomeCrates.Concat(new[] { "wooden_crate", "iron_crate", "golden_crate" }))
        {
            var item = g.Content.Items.Get(id);
            var info = g.World.Uses[item];
            Assert.Equal(id, info.Consume?.Loot);
            int items = 0;
            for (int i = 0; i < 40; i++) items += roller.Roll(id, ref rng, new ConditionContext()).Count;
            Assert.True(items >= 40, $"{id} gave only {items} stacks in 40 opens");
        }
    }

    [Fact]
    public void BiomeCratesNeedTheirBiome()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(5);
        HashSet<string> Crates(string table, params string[] biomes)
        {
            var ctx = new ConditionContext { Biomes = biomes, Layer = Strata.Core.World.WorldLayer.Surface };
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
                foreach (var s in roller.Roll(table, ref rng, ctx)) seen.Add(s.Item!.Id);
            return seen;
        }
        Assert.Equal(new[] { "iron_crate", "jungle_crate" }, Crates("fishing_crate_rare", "jungle").OrderBy(x => x));
        Assert.Equal(new[] { "iron_crate" }, Crates("fishing_crate_rare", "forest"));
        Assert.Contains("frozen_crate", Crates("fishing_crate_uncommon", "tundra"));
        Assert.Equal(new[] { "golden_crate" }, Crates("fishing_crate_very_rare", "forest"));
        Assert.Equal(new[] { "obsidian_crate" }, Crates("fishing_lava_crate", "underworld"));
    }

    [Fact]
    public void CratesContainBaitForTheNextCatch()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(9);
        bool bait = false;
        for (int i = 0; i < 200 && !bait; i++)
            foreach (var s in roller.Roll("jungle_crate", ref rng, new ConditionContext()))
                bait |= g.World.Uses[s.Item!].Bait != null;
        Assert.True(bait, "no bait in 200 jungle crates");
    }

    [Fact]
    public void CrittersAndBaitItemsHaveBaitPower()
    {
        using var g = TestGame.Create();
        int Power(string id) => g.World.Uses[g.Content.Items.Get(id)].Bait?.Power ?? 0;
        Assert.Equal(25, Power("worm"));
        Assert.Equal(10, Power("firefly"));
        Assert.Equal(50, Power("master_bait"));
        Assert.True(Power("apprentice_bait") < Power("journeyman_bait"));
        Assert.Equal(0, Power("bunny"));
    }
}
