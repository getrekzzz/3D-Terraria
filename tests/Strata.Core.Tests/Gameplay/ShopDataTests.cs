using Strata.Core.Common;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-15..P5-18: the residents' Terraria 1.4.4 shop data and its stock conditions.</summary>
public class ShopDataTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    internal static TownNpc Summon(TestGame g, string id) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    internal static List<string> Stock(TestGame g, TownNpc npc, string tab = "shop") =>
        g.World.Commerce.Stock(npc, g.Player, tab).Select(o => o.Item.Id).ToList();

    [Fact]
    public void EveryShopItemResolvesAndCostsSomething()
    {
        var db = TestPaths.Content;
        foreach (var npc in db.TownNpcs.All)
            foreach (var e in npc.Shop)
            {
                Assert.True(db.Items.TryGet(e.Item, out var item), $"{npc.Id} sells unknown '{e.Item}'");
                Assert.True((e.Price ?? item.Value) >= 1, $"{npc.Id}: '{e.Item}' is free");
                if (e.Currency != null) Assert.True(db.Items.Contains(e.Currency), $"{npc.Id}: unknown currency '{e.Currency}'");
            }
    }

    [Fact]
    public void TheNurseHasNoShop() => Assert.Empty(TestPaths.Content.TownNpcs.Get("nurse").Shop);

    [Fact]
    public void TheMerchantSellsGlowsticksAtNight()
    {
        using var g = Create();
        var merchant = Summon(g, "merchant");
        g.World.Session.Clock.SetHour(12);
        var day = Stock(g, merchant);
        Assert.Contains("mining_shirt", day);
        Assert.Contains("iron_anvil", day);
        Assert.DoesNotContain("glowstick", day);
        g.World.Session.Clock.SetHour(22);
        Assert.Contains("glowstick", Stock(g, merchant));
    }

    [Fact]
    public void TheDryadsSeedsFollowHardmodeAndTheWorldsEvil()
    {
        using var g = Create();
        var dryad = Summon(g, "dryad");
        var before = Stock(g, dryad);
        Assert.Contains("grass_seeds", before);
        Assert.Contains("sunflower", before);
        Assert.DoesNotContain("hallowed_seeds", before);
        Assert.DoesNotContain("corrupt_seeds", before);
        g.World.Progression.Set("hardmode");
        var after = Stock(g, dryad);
        Assert.Contains("hallowed_seeds", after);
        string evil = g.World.Session.Settings.Evil.ToString().ToLowerInvariant();
        Assert.Contains(evil == "crimson" ? "crimson_seeds" : "corrupt_seeds", after);
        Assert.DoesNotContain(evil == "crimson" ? "corrupt_seeds" : "crimson_seeds", after);
    }

    [Fact]
    public void TheArmsDealerSellsGunPartsAtNightAndBulletsInHardmode()
    {
        using var g = Create();
        var dealer = Summon(g, "arms_dealer");
        g.World.Session.Clock.SetHour(12);
        Assert.DoesNotContain("illegal_gun_parts", Stock(g, dealer));
        Assert.DoesNotContain("empty_bullet", Stock(g, dealer));
        g.World.Session.Clock.SetHour(22);
        g.World.Progression.Set("hardmode");
        Assert.Contains("illegal_gun_parts", Stock(g, dealer));
        Assert.Contains("empty_bullet", Stock(g, dealer));
    }

    [Fact]
    public void HappinessScalesCoinPrices()
    {
        using var g = Create();
        var merchant = Summon(g, "merchant");
        float factor = g.World.Commerce.PriceFactor(merchant);
        var offer = g.World.Commerce.Stock(merchant, g.Player).First(o => o.Item.Id == "mining_shirt");
        Assert.Equal(Math.Max(1, (long)MathF.Round(offer.Item.Value * factor)), offer.Price);
    }

    [Fact]
    public void ThePainterHasADecorTab()
    {
        using var g = Create();
        var painter = Summon(g, "painter");
        Assert.Equal(new[] { "shop", "decor" }, g.World.Commerce.Tabs(painter));
        Assert.Contains("red_paint", Stock(g, painter));
        var decor = Stock(g, painter, "decor");
        Assert.Contains("land", decor);
        Assert.DoesNotContain("the_truth_is_up_there", decor);
        Assert.All(decor, id => Assert.Equal(Strata.Core.Content.Defs.TileAnchor.Wall, g.Content.TileObjects.Get(id).Anchor));
        g.World.Progression.Set("defeated_martians");
        Assert.Contains("the_truth_is_up_there", Stock(g, painter, "decor"));
    }

    [Fact]
    public void TheTavernkeepSellsAleForMedals()
    {
        using var g = Create();
        var keep = Summon(g, "tavernkeep");
        var inv = g.Player.State.Inventory;
        var ale = g.Content.Items.Get("ale");
        var medal = g.Content.Items.Get("defender_medal");
        var offer = g.World.Commerce.Stock(keep, g.Player).Single(o => o.Item == ale);
        Assert.Equal(medal, offer.Currency);
        long coins = Strata.Core.Gameplay.Items.Coins.Total(inv);
        Assert.Equal(TradeResult.TooPoor, g.World.Commerce.Buy(keep, g.Player, ale));
        inv.Backpack.Add(new Strata.Core.Gameplay.Items.ItemStack(medal, 10));
        Assert.Equal(TradeResult.Ok, g.World.Commerce.Buy(keep, g.Player, ale));
        Assert.Equal(10 - offer.Price, inv.Backpack.Count(medal));
        Assert.Equal(1, inv.CountAll(ale));
        Assert.Equal(coins, Strata.Core.Gameplay.Items.Coins.Total(inv));
    }

    [Fact]
    public void TheOldOnesArmyDropsDefenderMedals()
    {
        var db = TestPaths.Content;
        foreach (var table in new[] { "old_ones_army", "dark_mage", "ogre", "betsy" })
            Assert.Contains(db.LootTables.Get(table).Entries, e => e.Item == "defender_medal");
    }
}
