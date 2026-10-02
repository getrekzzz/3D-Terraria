using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Terraria's prefix table (data/prefixes.json), where prefixes roll, tooltips, value and old saves.</summary>
public class PrefixTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = TestPaths.Content;

    private static PrefixDef P(string id) => Db.Prefixes.Get(id);

    [Fact]
    public void LegendaryHasTerrariasStats()
    {
        var p = P("legendary");
        Assert.Equal("Legendary", p.DisplayName);
        Assert.Equal(0.15f, p.Damage, 3);
        Assert.Equal(0.1f, p.UseSpeed, 3);
        Assert.Equal(5, p.Crit);
        Assert.Equal(0.1f, p.Size, 3);
        Assert.Equal(0.15f, p.Knockback, 3);
    }

    [Fact]
    public void TableMatchesKnownValuesPerGroup()
    {
        Assert.Equal(83, Db.Prefixes.All.Count(p => !p.HasTag("placeholder")));
        Assert.Equal(new[] { "universal" }, P("godly").Applies);
        Assert.Equal((0.15f, 5, 0.15f), (P("godly").Damage, P("godly").Crit, P("godly").Knockback));
        Assert.Equal((0.07f, 0.06f, 3), (P("murderous").Damage, P("murderous").UseSpeed, P("murderous").Crit));
        Assert.Equal(0.18f, P("massive").Size, 3);
        Assert.Equal((0.15f, 0.1f, 0.1f), (P("unreal").Damage, P("unreal").UseSpeed, P("unreal").Velocity));
        Assert.Equal((0.15f, -0.1f), (P("mythical").Damage, P("mythical").ManaCost));
        Assert.Equal(-0.15f, P("adept").ManaCost, 3);
        Assert.Equal(4, P("warding").Defense);
        Assert.Equal(0.04f, P("menacing").Damage, 3);
        Assert.Equal(0.04f, P("violent").MeleeSpeed, 3);
        Assert.Equal(0.04f, P("quick_accessory").MoveSpeed, 3);
        Assert.Equal("Deadly", P("deadly_ranged").DisplayName);
        Assert.Equal("Brisk", P("brisk_accessory").DisplayName);
    }

    [Fact]
    public void GroupsFollowTheWeaponKind()
    {
        Assert.Equal(new[] { "universal", "common", "melee" }, Prefixes.Groups(Db.Items.Get("copper_broadsword")));
        Assert.Equal(new[] { "universal", "common", "melee" }, Prefixes.Groups(Db.Items.Get("copper_pickaxe")));
        Assert.Equal(new[] { "universal", "common", "ranged" }, Prefixes.Groups(Db.Items.Get("wooden_bow")));
        Assert.Equal(new[] { "universal", "common", "magic" }, Prefixes.Groups(Db.Items.Get("amethyst_staff")));
        Assert.Equal(new[] { "accessory" }, Prefixes.Groups(Db.Items.Get("aglet")));
        // A spear can never roll a size prefix.
        var spear = Db.Items.Get("spear");
        Assert.Equal(new[] { "universal", "common" }, Prefixes.Groups(spear));
        var rng = new Pcg32(7);
        for (int i = 0; i < 300; i++) Assert.Equal(0f, Prefixes.Roll(spear, Db, ref rng)!.Size);
    }

    [Fact]
    public void ValueUsesTerrariasSquaredMultiplier()
    {
        var sword = Db.Items.Get("copper_broadsword");
        float mul = 1.15f * 1.1f * 1.1f * 1.15f * 1.1f;
        Assert.Equal((long)MathF.Round(sword.Value * mul * mul), Prefixes.Value(new ItemStack(sword, 1, P("legendary"))));
        var boots = Db.Items.Get("aglet");
        Assert.Equal((long)MathF.Round(boots.Value * 1.2f * 1.2f), Prefixes.Value(new ItemStack(boots, 1, P("warding"))));
        Assert.True(Prefixes.Value(new ItemStack(sword, 1, P("broken"))) < sword.Value);
    }

    [Fact]
    public void LootDropsUsuallyRollAPrefix()
    {
        var roller = new LootRoller(Db);
        var rng = new Pcg32(42);
        int swords = 0, prefixed = 0;
        for (int i = 0; i < 300 && swords < 100; i++)
            foreach (var s in roller.Roll("enchanted_sword_shrine", ref rng).Where(s => s.Item!.Id == "enchanted_sword"))
            {
                swords++;
                if (s.Prefix != null) prefixed++;
            }
        Assert.True(swords >= 50, $"only {swords} swords");
        Assert.True(prefixed >= swords * 0.6f, $"{prefixed} of {swords} prefixed");
    }

    [Fact]
    public void ShopBuysCanCarryAPrefix()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var merchant = g.World.Town.Summon(g.Content.TownNpcs.Get("merchant"),
            g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
        var inv = g.Player.State.Inventory.Backpack;
        Assert.Empty(Wallet.Give(inv, g.Content, 1000000));
        var pick = g.Content.Items.Get("copper_pickaxe");
        for (int i = 0; i < 12; i++) Assert.Equal(TradeResult.Ok, g.World.Commerce.Buy(merchant, g.Player, pick));
        Assert.Contains(inv.Stacks, s => s.Item == pick && s.Prefix != null);
    }

    [Fact]
    public void TooltipShowsModifierLinesAndPrefixedStats()
    {
        var sword = Db.Items.Get("copper_broadsword");
        var stack = new ItemStack(sword, 1, P("legendary"));
        var lines = ItemTooltip.Lines(stack, new ItemUseTable(Db)[sword], usedInRecipes: false);
        Assert.Equal("Legendary Copper Broadsword", lines[0].Text);
        Assert.Contains(("+15% damage", TooltipKind.Modifier), lines);
        Assert.Contains(("+10% speed", TooltipKind.Modifier), lines);
        Assert.Contains(lines, l => l.Text == $"{Prefixes.Damage(stack)} melee damage");
        Assert.Contains(lines, l => l.Text == $"Sell price: {Coins.Format(Prefixes.Value(stack) / 5)}");
        var bad = ItemTooltip.Lines(new ItemStack(sword, 1, P("sluggish")), new ItemUseTable(Db)[sword], false);
        Assert.Contains(("-20% speed", TooltipKind.ModifierBad), bad);
        Assert.Contains(("-15% mana cost", TooltipKind.Modifier), ItemTooltip.Modifiers(P("adept")));
    }

    [Fact]
    public void OldSavesWithInventedPrefixesLoadToTerrariasPrefixes()
    {
        // A character saved before P3-31 with every removed invented id; items always survive.
        var json = """
            { "format": "strata-player", "version": 1, "name": "Old", "life": 100, "mana": 20,
              "backpack": [
                { "slot": 0, "item": "iron_broadsword", "count": 1, "prefix": "mythic" },
                { "slot": 1, "item": "wooden_sword", "count": 1, "prefix": "stout" },
                { "slot": 2, "item": "copper_broadsword", "count": 1, "prefix": "brisk" },
                { "slot": 3, "item": "copper_broadsword", "count": 1, "prefix": "towering" },
                { "slot": 4, "item": "copper_broadsword", "count": 1, "prefix": "stubby" },
                { "slot": 5, "item": "wooden_bow", "count": 1, "prefix": "farsighted" },
                { "slot": 6, "item": "wooden_bow", "count": 1, "prefix": "unerring" },
                { "slot": 7, "item": "amethyst_staff", "count": 1, "prefix": "transcendent" },
                { "slot": 8, "item": "amethyst_staff", "count": 1, "prefix": "frugal" },
                { "slot": 9, "item": "copper_pickaxe", "count": 1, "prefix": "swift_tool" },
                { "slot": 10, "item": "amethyst_staff", "count": 1, "prefix": "arcane" },
                { "slot": 11, "item": "iron_broadsword", "count": 1, "prefix": "keen" },
                { "slot": 12, "item": "wooden_sword", "count": 1, "prefix": "warding" },
                { "slot": 13, "item": "wooden_sword", "count": 1, "prefix": "never_existed" } ],
              "accessories": [
                { "slot": 0, "item": "aglet", "count": 1, "prefix": "sturdy" },
                { "slot": 1, "item": "band_of_regeneration", "count": 1, "prefix": "fleet" },
                { "slot": 2, "item": "hermes_boots", "count": 1, "prefix": "lucky" } ] }
            """;
        var state = PlayerSave.Deserialize(json).ToState(Db);
        var bag = state.Inventory.Backpack;
        string?[] expected =
        {
            "legendary", "unpleasant", "quick", "massive", "tiny", "sighted", "unreal", "mythical", "adept", "quick",
            null, "keen", null, "never_existed",
        };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.False(bag[i].IsEmpty, $"slot {i} lost its item");
            Assert.Equal(expected[i], bag[i].Prefix?.Id);
        }
        Assert.Equal("guarding", state.Inventory.Accessories[0].Prefix?.Id);
        Assert.Equal("fleeting", state.Inventory.Accessories[1].Prefix?.Id);
        Assert.Equal("lucky", state.Inventory.Accessories[2].Prefix?.Id);
        // The mapped prefixes save under their new ids.
        var again = PlayerSave.Deserialize(PlayerSave.From(state).Serialize()).ToState(Db);
        Assert.Equal("legendary", again.Inventory.Backpack[0].Prefix!.Id);
    }
}
