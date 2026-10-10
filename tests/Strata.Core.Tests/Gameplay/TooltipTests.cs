using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class TooltipTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = TestPaths.Content;
    private static readonly ItemUseTable Uses = new(Db);

    private static List<(string Text, TooltipKind Kind)> Lines(ItemStack stack, PlayerState? player = null, long? buy = null) =>
        ItemTooltip.Lines(stack, Uses[stack.Item!], new TooltipContext(false, Db, player, 0.2f, buy));

    private static ItemStack Sword(string? prefix) =>
        new(Db.Items.Get("copper_broadsword"), 1, prefix != null ? Db.Prefixes.Get(prefix) : null);

    [Fact]
    public void PrefixChangesStatsAndAddsGreenRedLines()
    {
        var plain = Lines(Sword(null));
        var broken = Lines(Sword("broken"));
        Assert.Equal("Broken Copper Broadsword", broken[0].Text);
        Assert.NotEqual(plain[1].Text, broken[1].Text); // the damage line drops by 30 %
        Assert.Equal($"{Prefixes.Damage(Sword("broken"))} melee damage", broken[1].Text);
        Assert.Contains(("-30% damage", TooltipKind.Bad), broken);
        Assert.Contains(("-20% knockback", TooltipKind.Bad), broken);
        Assert.DoesNotContain(plain, l => l.Kind is TooltipKind.Good or TooltipKind.Bad);

        var keen = Lines(Sword("keen"));
        Assert.Contains(("+3% critical strike chance", TooltipKind.Good), keen);
        Assert.Contains($"{4 + Db.Items.Get("copper_broadsword").Crit + 3}% critical strike chance", keen.Select(l => l.Text));

        var ruthless = Lines(Sword("ruthless"));
        Assert.Contains(("+18% damage", TooltipKind.Good), ruthless);
        Assert.Contains(("-10% knockback", TooltipKind.Bad), ruthless);

        var adept = new ItemStack(Db.Items.All.First(i => i.ManaCost > 0 && Prefixes.Groups(i).Contains("magic")), 1, Db.Prefixes.Get("adept"));
        Assert.Contains(("-15% mana cost", TooltipKind.Good), Lines(adept)); // less mana is better
    }

    [Fact]
    public void PlayerClassBonusRaisesDamageAndCrit()
    {
        var player = new PlayerState("t");
        player.Effects.Stats.Add(StatIds.MeleeDamage, 0.5f);
        player.Effects.Stats.Add(StatIds.GenericCrit, 10f);
        var sword = Sword(null);
        var lines = Lines(sword, player).Select(l => l.Text).ToList();
        Assert.Equal($"{(int)MathF.Round(sword.Item!.Damage * 1.5f)} melee damage", lines[1]);
        Assert.Equal($"{4 + sword.Item.Crit + 10}% critical strike chance", lines[2]);
    }

    [Fact]
    public void PotionShowsBuffEffectAndDuration()
    {
        var ironskin = Lines(new ItemStack(Db.Items.Get("ironskin_potion"), 1)).Select(l => l.Text).ToList();
        Assert.Contains("Increase defense by 8", ironskin);
        Assert.Contains("5 minute duration", ironskin);
        Assert.True(ironskin.IndexOf("Increase defense by 8") < ironskin.IndexOf("5 minute duration"));
        var swiftness = Lines(new ItemStack(Db.Items.Get("swiftness_potion"), 1)).Select(l => l.Text).ToList();
        Assert.Contains("25% increased movement speed", swiftness);
        Assert.Contains("4 minute duration", swiftness);
    }

    [Fact]
    public void BuffTextDescribesEffectsInTerrariaWording()
    {
        Assert.Equal("+8 defense", BuffText.FromEffects(Db.Buffs.Get("ironskin"), Db));
        Assert.Equal("25% increased movement speed", BuffText.FromEffects(Db.Buffs.Get("swiftness"), Db));
        Assert.Equal("Cannot regenerate life", BuffText.FromEffects(Db.Buffs.Get("bleeding"), Db));
        Assert.Equal("Slowly losing life\nEmitting light", BuffText.FromEffects(Db.Buffs.Get("on_fire"), Db));
        Assert.Equal("Immune to Chilled, Frozen and Frostburn", BuffText.FromEffects(Db.Buffs.Get("warmth"), Db));
        Assert.Equal("20% increased maximum life", BuffText.FromEffects(Db.Buffs.Get("lifeforce"), Db));
        Assert.Equal("25% reduced magic damage", BuffText.FromEffects(Db.Buffs.Get("mana_sickness"), Db));
        Assert.Equal("Increase defense by 8", BuffText.Describe(Db.Buffs.Get("ironskin"), Db)); // data wording wins
        Assert.Null(BuffText.Describe(new BuffDef { Id = "blank" }));
    }

    [Fact]
    public void ZeroValueSaysNoValue()
    {
        var junk = new ItemDef { Id = "junk", Name = "Junk" };
        var lines = ItemTooltip.Lines(new ItemStack(junk, 1), new ItemUseInfo(junk), usedInRecipes: true);
        Assert.Equal(("No value", TooltipKind.NoValue), lines[^1]);
        Assert.Contains(("Material", TooltipKind.Info), lines);
    }

    [Fact]
    public void StackCountInName()
    {
        var torch = Db.Items.Get("torch");
        Assert.Equal("Torch (25)", Lines(new ItemStack(torch, 25))[0].Text);
        Assert.Equal("Torch", Lines(new ItemStack(torch, 1))[0].Text);
        Assert.Contains("Can be placed", Lines(new ItemStack(torch, 25)).Select(l => l.Text));
    }

    [Fact]
    public void SetBonusOnlyWhenWorn()
    {
        var set = Db.Items.All.Where(i => i.ArmorSet == "copper_armor").OrderBy(i => i.EquipSlot).ToList();
        var head = set.Single(i => i.SetBonus != null);
        var player = new PlayerState("t");
        bool HasBonus() => Lines(new ItemStack(head, 1), player).Any(l => l.Text.StartsWith("Set bonus:"));

        Assert.False(HasBonus());
        Assert.DoesNotContain(ItemTooltip.Lines(new ItemStack(head, 1), Uses[head], usedInRecipes: false), l => l.Text.StartsWith("Set bonus:"));
        player.Inventory.Armor[0] = new ItemStack(set.Single(i => i.EquipSlot == EquipSlot.Head), 1);
        player.Inventory.Armor[1] = new ItemStack(set.Single(i => i.EquipSlot == EquipSlot.Body), 1);
        Assert.False(HasBonus()); // two pieces are not a set
        player.Inventory.Armor[2] = new ItemStack(set.Single(i => i.EquipSlot == EquipSlot.Legs), 1);
        Assert.True(HasBonus());
    }

    [Fact]
    public void PriceKindFollowsTopCoin()
    {
        Assert.Equal(TooltipKind.PriceCopper, ItemTooltip.PriceKind(99));
        Assert.Equal(TooltipKind.PriceSilver, ItemTooltip.PriceKind(100));
        Assert.Equal(TooltipKind.PriceGold, ItemTooltip.PriceKind(25_000));
        Assert.Equal(TooltipKind.PricePlatinum, ItemTooltip.PriceKind(1_000_000));
        Assert.Equal(new CoinParts(1, 2, 3, 4), Coins.Parts(1_020_304));
        Assert.Equal(CoinTier.Copper, Coins.TopTier(0));

        var sword = Lines(Sword(null), buy: 25_000);
        Assert.Equal(("Buy price: 2 gold 50 silver", TooltipKind.PriceGold), sword[^1]);
        var torches = Lines(new ItemStack(Db.Items.Get("torch"), 5));
        Assert.Equal(TooltipKind.PriceCopper, torches[^1].Kind);
        Assert.StartsWith("Sell price: ", torches[^1].Text);
    }

    [Fact]
    public void TooltipSellPriceEqualsCommerceSaleValue()
    {
        using var g = TestGame.Create();
        var ctx = new TooltipContext(false, g.Content, g.Player.State, g.World.Town.Config.SellRatio);
        var uses = new ItemUseTable(g.Content);
        foreach (var stack in new[]
                 {
                     new ItemStack(g.Content.Items.Get("copper_bar"), 10),
                     new ItemStack(g.Content.Items.Get("torch"), 3),
                     new ItemStack(g.Content.Items.Get("copper_broadsword"), 1, g.Content.Prefixes.Get("ruthless")),
                     new ItemStack(g.Content.Items.Get("copper_broadsword"), 1, g.Content.Prefixes.Get("broken")),
                 })
        {
            long value = g.World.Commerce.SaleValue(stack);
            Assert.True(value > 0);
            Assert.Equal($"Sell price: {Coins.Format(value)}", ItemTooltip.Lines(stack, uses[stack.Item!], ctx)[^1].Text);
        }
    }
}
