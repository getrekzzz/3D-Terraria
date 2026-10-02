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

    /// <summary>Residents without goods in Terraria (or whose goods do not exist yet: the Stylist's hair dyes).</summary>
    private static readonly string[] ServiceOnly = { "guide", "nurse", "old_man", "tax_collector", "stylist" };

    [Fact]
    public void EveryResidentExceptServiceOnlyOnesHasAShop()
    {
        foreach (var npc in TestPaths.Content.TownNpcs.All)
            if (Array.IndexOf(ServiceOnly, npc.Id) < 0 && !npc.Pet) Assert.True(npc.AllShopEntries.Length > 0, $"{npc.Id} sells nothing");
    }

    [Fact]
    public void SantaSellsOnlyAtChristmas()
    {
        using var g = Create();
        var santa = Summon(g, "santa_claus");
        g.World.Session.Seasons.Override = "";
        Assert.Empty(Stock(g, santa));
        g.World.Session.Seasons.Override = "christmas";
        var stock = Stock(g, santa);
        Assert.Contains("christmas_tree", stock);
        Assert.Contains("star_topper", stock);
    }

    [Fact]
    public void TheCyborgsRocketsFollowTheNightAndTheBloodMoon()
    {
        using var g = Create();
        var cyborg = Summon(g, "cyborg");
        g.World.Session.Clock.SetHour(12);
        var day = Stock(g, cyborg);
        Assert.Contains("rocket_i", day);
        Assert.DoesNotContain("rocket_ii", day);
        Assert.DoesNotContain("rocket_iii", day);
        g.World.Session.Clock.SetHour(22);
        Assert.Contains("rocket_iii", Stock(g, cyborg));
    }

    private sealed class FakeRecords : Strata.Core.Conditions.IPlayerRecords
    {
        public float BestiaryPercent { get; set; }
        public int Kills(string enemy) => 0;
        public bool Obtained(string item) => false;
        public bool Achieved(string id) => false;
    }

    [Fact]
    public void TheZoologistsStockGrowsWithTheBestiary()
    {
        using var g = Create();
        var records = new FakeRecords { BestiaryPercent = 9 };
        g.World.Town.PlayerRecords = _ => records;
        var zoologist = Summon(g, "zoologist");
        Assert.DoesNotContain("leather_whip", Stock(g, zoologist));
        records.BestiaryPercent = 10;
        Assert.Equal(new[] { "leather_whip" }, Stock(g, zoologist));
        records.BestiaryPercent = 50;
        Assert.Contains("fairy_glowstick", Stock(g, zoologist));
        Assert.Contains("critter_shampoo", Stock(g, zoologist));
    }

    [Fact]
    public void TheGolferSellsClubsBallsAndCourseObjects()
    {
        using var g = Create();
        var stock = Stock(g, Summon(g, "golfer"));
        foreach (var id in new[] { "golf_club_iron", "golf_club_driver", "golf_club_wedge", "golf_club_putter", "golf_ball", "golf_cup", "golf_tee" })
            Assert.Contains(id, stock);
    }
}
