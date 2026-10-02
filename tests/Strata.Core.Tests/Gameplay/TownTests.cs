using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class TownTests
{
    private static TestGame Create(string? saveDir = null)
    {
        var g = TestGame.Create(saveDir);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false; // tests run the move-in check explicitly
        return g;
    }

    private static TownNpc Summon(TestGame g, string id, Int3 offset) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + offset, Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    private static void GiveCoins(TestGame g, long copper) =>
        Assert.Empty(Wallet.Give(g.Player.State.Inventory, g.Content, copper));

    private static long Purse(TestGame g) => Coins.Total(g.Player.State.Inventory);

    [Fact]
    public void GuideStartsInTownAndResidentsMoveIntoValidHomes()
    {
        using var g = Create();
        var town = g.World.Town;
        g.Ticks(1);
        var guide = town.Find("guide");
        Assert.NotNull(guide);
        Assert.Null(guide!.Home);
        Assert.True(g.World.Session.Meta.TownStarted);

        TestHouses.Build(g, g.Floor + new Int3(-6, 0, 0));
        Assert.Null(town.CheckArrivals()); // the homeless guide takes the first home
        Assert.NotNull(guide.Home);

        TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        Assert.Null(town.CheckArrivals()); // the peddler wants to see 50 silver first
        Assert.Null(town.Find("merchant"));
        GiveCoins(g, 5000);
        var peddler = town.CheckArrivals();
        Assert.Equal("merchant", peddler?.Def.Id);
        Assert.NotNull(peddler!.Home);
        Assert.NotEqual(guide.HomeRoom, peddler.HomeRoom);
        Assert.Contains(peddler.Name, g.Content.TownNpcs.Get("merchant").Names);

        // No vacant home left: nobody else can move in, whatever their conditions.
        g.Player.State.BonusLife = 40;
        g.Player.State.Recompute();
        Assert.Null(town.CheckArrivals());
        Assert.Null(town.Find("nurse"));
    }

    [Fact]
    public void BreakingAHomeMakesTheResidentHomeless()
    {
        using var g = Create();
        var town = g.World.Town;
        var guide = Summon(g, "guide", new Int3(0, 1, -9));
        TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        town.CheckArrivals();
        Assert.NotNull(guide.Home);
        var lost = new List<TownNpc>();
        town.HomeLost += lost.Add;
        g.World.Editor.SetBlock(g.Floor + new Int3(11, 3, 0), 0); // hole in the east wall
        g.World.Session.Clock.SetHour(12);
        do g.World.Tick();
        while (g.World.Session.Clock.Tick % town.Config.HomeCheckInterval != 0);
        Assert.Contains(guide, lost);
        Assert.Null(guide.Home);
    }

    [Fact]
    public void ShopSellsBuysAndMakesChange()
    {
        using var g = Create();
        var peddler = Summon(g, "merchant", new Int3(2, 1, 2));
        var commerce = g.World.Commerce;
        var stock = commerce.Stock(peddler, g.Player);
        var torch = stock.Single(o => o.Item.Id == "torch");
        Assert.DoesNotContain(stock, o => o.Item.Id == "acorn"); // needs the first boss down

        GiveCoins(g, 100);
        int torches = g.Player.State.Inventory.Backpack.Count(torch.Item);
        Assert.Equal(TradeResult.Ok, commerce.Buy(peddler, g.Player, torch.Item));
        Assert.Equal(100 - torch.Price, Purse(g));
        Assert.Equal(torches + 1, g.Player.State.Inventory.Backpack.Count(torch.Item));
        Assert.Equal(TradeResult.TooPoor, commerce.Buy(peddler, g.Player, g.Content.Items.Get("copper_pickaxe")));

        var inv = g.Player.State.Inventory.Backpack;
        inv[20] = new ItemStack(g.Content.Items.Get("copper_bar"), 10);
        long before = Purse(g);
        Assert.Equal(TradeResult.Ok, commerce.Sell(g.Player, 20));
        Assert.True(inv[20].IsEmpty);
        Assert.Equal(before + 150, Purse(g)); // 20 % of 75 copper × 10
    }

    [Fact]
    public void BuyIntoCursorMergesSameItem()
    {
        using var g = Create();
        var peddler = Summon(g, "merchant", new Int3(2, 1, 2));
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        var torch = commerce.Stock(peddler, g.Player).Single(o => o.Item.Id == "torch");
        GiveCoins(g, 1000);
        int carried = inv.CountAll(torch.Item);
        Assert.Equal(TradeResult.Ok, commerce.Buy(peddler, g.Player, torch.Item, 1, BuyTarget.Cursor));
        Assert.Equal(TradeResult.Ok, commerce.Buy(peddler, g.Player, torch.Item, 1, BuyTarget.Cursor));
        Assert.Equal(new ItemStack(torch.Item, 2), inv.Cursor); // repeat clicks stack on the cursor
        Assert.Equal(carried, inv.CountAll(torch.Item)); // the backpack is untouched
        Assert.Equal(1000 - 2 * torch.Price, Purse(g));
    }

    [Fact]
    public void CursorHoldingOtherItemRefuses()
    {
        using var g = Create();
        var peddler = Summon(g, "merchant", new Int3(2, 1, 2));
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        GiveCoins(g, 1000);
        var bar = new ItemStack(g.Content.Items.Get("copper_bar"), 3);
        inv.Cursor = bar;
        Assert.Equal(TradeResult.NoRoom, commerce.Buy(peddler, g.Player, g.Content.Items.Get("torch"), 1, BuyTarget.Cursor));
        Assert.Equal(bar, inv.Cursor);
        Assert.Equal(1000, Purse(g));
    }

    [Fact]
    public void SoldStackCanBeBoughtBackAtSalePrice()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        var bars = new ItemStack(g.Content.Items.Get("copper_bar"), 10);
        inv.Backpack[20] = bars;
        Assert.Equal(TradeResult.Ok, commerce.Sell(g.Player, inv.Backpack, 20));
        Assert.Equal(150, Purse(g)); // 20 % of 75 copper × 10
        Assert.Equal(new[] { bars }, commerce.Buyback(g.Player));

        // Dropped from the cursor: the newest sale comes first. An item of no value goes for nothing.
        var wood = new ItemStack(g.Content.Items.Get("wood"), 10);
        inv.Cursor = wood;
        Assert.Equal(TradeResult.Ok, commerce.SellCursor(g.Player));
        Assert.True(inv.Cursor.IsEmpty);
        Assert.Equal(150, Purse(g));
        Assert.Equal(new[] { wood, bars }, commerce.Buyback(g.Player));

        Assert.Equal(TradeResult.Ok, commerce.BuyBack(g.Player, 1, BuyTarget.Backpack));
        Assert.Equal(0, Purse(g));
        Assert.Equal(10, inv.Backpack.Count(bars.Item!));
        Assert.Equal(TradeResult.Ok, commerce.BuyBack(g.Player, 0, BuyTarget.Cursor));
        Assert.Equal(wood, inv.Cursor);
        Assert.Empty(commerce.Buyback(g.Player));
        Assert.Equal(TradeResult.Unavailable, commerce.BuyBack(g.Player, 0));
    }

    [Fact]
    public void SellingAFavoriteIsRefused()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        var bars = new ItemStack(g.Content.Items.Get("copper_bar"), 10);
        inv.Backpack[20] = bars;
        inv.Backpack.SetFavorite(20, true);
        Assert.Equal(TradeResult.NotSold, commerce.Sell(g.Player, inv.Backpack, 20));
        Assert.Equal(bars, inv.Backpack[20]);
        inv.Cursor = new ItemStack(g.Content.Items.Get("silver_coin"), 5); // coins never sell either
        Assert.Equal(TradeResult.NotSold, commerce.SellCursor(g.Player));
        Assert.Equal(5, inv.Cursor.Count);
        Assert.Empty(commerce.Buyback(g.Player));
        Assert.Equal(0, Purse(g));
    }

    [Fact]
    public void BuybackCappedAt40()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        var bar = g.Content.Items.Get("copper_bar");
        for (int i = 1; i <= TownCommerce.BuybackCapacity + 5; i++)
        {
            inv.Backpack[20] = new ItemStack(bar, i);
            Assert.Equal(TradeResult.Ok, commerce.Sell(g.Player, 20));
        }
        var buyback = commerce.Buyback(g.Player);
        Assert.Equal(TownCommerce.BuybackCapacity, buyback.Count);
        Assert.Equal(45, buyback[0].Count); // newest first; the five oldest fell off
        Assert.Equal(6, buyback[^1].Count);
    }

    [Fact]
    public void MenderHealsAndCuresForCoins()
    {
        using var g = Create();
        var mender = Summon(g, "nurse", new Int3(2, 1, 2));
        var commerce = g.World.Commerce;
        var state = g.Player.State;
        Assert.Equal(TradeResult.NothingToDo, commerce.Heal(mender, g.Player));
        state.Vitals.Life = 40;
        state.Buffs.Add(g.Content.Buffs.Get("poisoned"), 600);
        state.Recompute();
        long cost = commerce.HealCost(mender, g.Player);
        Assert.True(cost > 0);
        Assert.Equal(TradeResult.TooPoor, commerce.Heal(mender, g.Player));
        GiveCoins(g, 10_000);
        Assert.Equal(TradeResult.Ok, commerce.Heal(mender, g.Player));
        Assert.Equal(state.Vitals.MaxLife, state.Vitals.Life);
        Assert.False(state.Buffs.Has("poisoned"));
        Assert.Equal(10_000 - cost, Purse(g));
    }

    [Fact]
    public void NeighboursChangePricesAndLinesNameResidents()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var peddler = Summon(g, "merchant", new Int3(2, 1, 2));
        float alone = commerce.PriceFactor(peddler);
        Summon(g, "tax_collector", new Int3(-2, 1, 2)); // the Merchant dislikes the Tax Collector
        Assert.True(commerce.PriceFactor(peddler) > alone);
        Assert.InRange(commerce.PriceFactor(peddler), g.World.Town.Config.MinPriceFactor, g.World.Town.Config.MaxPriceFactor);

        for (int i = 0; i < 20; i++)
        {
            var line = commerce.Greet(peddler, g.Player);
            Assert.DoesNotContain("{", line);
            Assert.False(string.IsNullOrWhiteSpace(line));
        }
    }

    [Fact]
    public void ResidentsWalkHomeAtNightThroughTheDoorAndCloseIt()
    {
        using var g = Create();
        var town = g.World.Town;
        var door = TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        var guide = Summon(g, "guide", new Int3(7, 1, -9));
        town.CheckArrivals();
        var home = guide.Home!.Value;
        g.World.Session.Clock.SetHour(21);
        bool doorOpened = false;
        for (int i = 0; i < 60 * 60 && Vector3.Distance(guide.Position, TownSystem.NodeFeet(home)) > 0.6f; i++)
        {
            g.World.Tick();
            doorOpened |= door.Active;
        }
        Assert.True(Vector3.Distance(guide.Position, TownSystem.NodeFeet(home)) <= 0.6f, $"guide stopped at {guide.Position}, home {TownSystem.NodeFeet(home)}");
        Assert.True(doorOpened);
        g.Ticks(120);
        Assert.False(door.Active); // closed behind them
    }

    [Fact]
    public void CreaturesHurtResidentsWhoReturnAfterDying()
    {
        using var g = Create();
        var town = g.World.Town;
        var guide = Summon(g, "guide", new Int3(4, 1, 4));
        var brute = g.Content.Enemies.All.First(e => e.ContactDamage && e.Damage > 0 && e.Boss == null && e.Gravity);
        g.World.Npcs.Spawn(brute, guide.Position);
        for (int i = 0; i < 120 && guide.Life == guide.Def.Life; i++) g.World.Tick();
        Assert.True(guide.Life < guide.Def.Life);

        var died = new List<TownNpc>();
        town.Died += died.Add;
        g.World.Npcs.Clear();
        town.Damage(guide, 100_000, guide.Position);
        Assert.False(guide.Alive);
        Assert.Contains(guide, died);
        Assert.Equal(g.World.Session.Clock.Tick, guide.DiedAtTick);
        town.Config = new TownConfig { RespawnAtDawn = false, RespawnTicks = 3 }; // the fixed-delay mode
        g.Ticks(4);
        Assert.True(guide.Alive);
        Assert.Equal(guide.Def.Life, guide.Life);
    }

    [Fact]
    public void HousedResidentsMakeATownThatSlowsSpawning()
    {
        using var g = Create();
        var town = g.World.Town;
        Summon(g, "guide", new Int3(0, 1, -9));
        Summon(g, "merchant", new Int3(2, 1, -9));
        Assert.False(g.World.Spawner.InTown(g.Player)); // homeless residents do not count
        TestHouses.Build(g, g.Floor + new Int3(-6, 0, 0));
        TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        town.CheckArrivals();
        Assert.Equal(2, town.Residents.Count(r => r.Home != null));
        Assert.True(g.World.Spawner.InTown(g.Player));
    }

    [Fact]
    public void ResidentsAreSavedWithTheWorld()
    {
        var dir = Path.Combine(Path.GetTempPath(), "strata-town-" + Guid.NewGuid().ToString("N"));
        try
        {
            string name;
            Int3? home;
            using (var g = Create(dir))
            {
                var guide = Summon(g, "guide", new Int3(0, 1, -9));
                TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
                g.World.Town.CheckArrivals();
                g.Ticks(1);
                name = guide.Name;
                home = guide.Home;
                g.World.SaveAll();
            }
            var session = WorldSession.Load(TestPaths.Content, dir, new RenderTables(TestPaths.Content, _ => 0), new MemoryLog());
            using var world = new GameWorld(session, new StreamingSettings(), 1);
            var restored = Assert.Single(world.Town.Residents);
            Assert.Equal("guide", restored.Def.Id);
            Assert.Equal(name, restored.Name);
            Assert.Equal(home, restored.Home);
            Assert.True(session.Meta.TownStarted);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
