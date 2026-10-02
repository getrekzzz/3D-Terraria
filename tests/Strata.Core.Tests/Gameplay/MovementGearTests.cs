using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class MovementGearTests
{
    private static PlayerState Wearing(params string[] ids)
    {
        var db = TestPaths.Content;
        var p = PlayerState.CreateNew("hero", db);
        foreach (var id in ids) p.Inventory.Backpack.Add(new ItemStack(db.Items.Get(id), 1));
        for (int i = 0; i < p.Inventory.Backpack.Size; i++)
            if (!p.Inventory.Backpack[i].IsEmpty && p.Inventory.Backpack[i].Item!.EquipSlot == EquipSlot.Accessory) p.Inventory.Equip(i);
        p.Recompute();
        return p;
    }

    [Fact]
    public void WingsBootsJumpsAndDashesCombine()
    {
        var p = Wearing("fledgling_wings", "rocket_boots", "cloud_in_a_balloon", "shield_of_cthulhu");
        var fx = p.Effects;
        Assert.NotNull(fx.Wings);
        Assert.True(fx.Wings!.Glide > 0);
        Assert.Equal(25, fx.Wings.Time);
        Assert.NotNull(fx.RocketBoots);
        Assert.True(fx.RocketBoots!.Boots);
        Assert.Single(fx.ExtraJumps);
        Assert.Equal(0.33f, fx.Stats.Bonus(StatIds.JumpHeight), 3);
        Assert.NotNull(fx.Dash);
    }

    [Fact]
    public void TheBestBootsWin()
    {
        var p = Wearing("rocket_boots", "lightning_boots");
        Assert.Equal(110, p.Effects.RocketBoots!.Time);
        Assert.Equal(0.55f, p.Effects.Stats.Bonus(StatIds.MoveSpeed), 3);
        p.Inventory.Accessories[0] = ItemStack.Empty;
        p.Inventory.Accessories[1] = ItemStack.Empty;
        p.Recompute();
        Assert.Null(p.Effects.RocketBoots);
    }

    [Fact]
    public void HooksAreGrapplesWithGrowingReach()
    {
        var db = TestPaths.Content;
        var uses = new ItemUseTable(db);
        var basic = uses[db.Items.Get("grappling_hook")];
        Assert.True(basic.Has(ItemAction.Grapple));
        Assert.NotNull(basic.Grapple);
        float last = 0;
        foreach (var id in new[] { "amethyst_hook", "topaz_hook", "sapphire_hook", "emerald_hook", "ruby_hook", "diamond_hook" })
        {
            float reach = uses[db.Items.Get(id)].Grapple!.Reach;
            Assert.True(reach > last, id);
            last = reach;
        }
        Assert.Null(uses[db.Items.Get("copper_pickaxe")].Grapple);
    }

    [Fact]
    public void ShieldOfCthulhuIsAnExpertDrop()
    {
        var db = TestPaths.Content;
        var loot = new LootRoller(db);
        // Expert and Master: it comes in the treasure bag, never loose; Classic and Journey never see it.
        bool Drops(int difficulty, string item)
        {
            var rng = new Pcg32(7);
            return loot.Roll("eye_of_cthulhu", ref rng, new ConditionContext { Difficulty = difficulty }).Any(s => s.Item!.Id == item);
        }
        Assert.False(Drops(0, "shield_of_cthulhu"));
        Assert.False(Drops(3, "eye_of_cthulhu_treasure_bag"));
        Assert.True(Drops(1, "eye_of_cthulhu_treasure_bag"));
        Assert.True(Drops(2, "eye_of_cthulhu_treasure_bag"));
        var bag = new Pcg32(7);
        Assert.Contains(loot.Roll("eye_of_cthulhu_bag", ref bag), s => s.Item!.Id == "shield_of_cthulhu");
    }

    [Fact]
    public void BiomeChestsHoldTheirOwnAccessories()
    {
        var db = TestPaths.Content;
        var loot = new LootRoller(db);
        HashSet<string> Found(string biome)
        {
            var found = new HashSet<string>();
            var rng = new Pcg32(3);
            var ctx = new ConditionContext { Biomes = new[] { biome } };
            for (int i = 0; i < 200; i++)
                foreach (var s in loot.Roll("cabin_chest", ref rng, ctx)) found.Add(s.Item!.Id);
            return found;
        }
        Assert.Contains("anklet_of_the_wind", Found("jungle"));
        Assert.DoesNotContain("anklet_of_the_wind", Found("forest"));
        Assert.Contains("blizzard_in_a_bottle", Found("tundra"));
        Assert.Contains("sandstorm_in_a_bottle", Found("desert"));
        Assert.Contains("cobalt_shield", Found("dungeon"));
    }
}
