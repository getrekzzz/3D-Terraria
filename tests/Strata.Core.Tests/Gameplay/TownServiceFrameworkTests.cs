using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-13: several services per NPC, shop tabs and currencies, the Nurse's progression pricing, world status.</summary>
public class TownServiceFrameworkTests
{
    private static TypedSpec Spec(string json) => System.Text.Json.JsonSerializer.Deserialize<TypedSpec>(json, ContentJson.Options)!;

    private static TestGame Create(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Summon(TestGame g, TownNpcDef def) =>
        g.World.Town.Summon(def, g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    [Fact]
    public void MultipleServicesAndTabs()
    {
        var def = new TownNpcDef
        {
            Id = "test_trader", Names = new[] { "Ty" },
            Service = Spec("""{"type":"reforge"}"""),
            Services = new[] { Spec("""{"type":"worldStatus"}""") },
            Shop = new[] { new ShopEntry { Item = "torch" }, new ShopEntry { Item = "gel", Tab = "decor" } },
        };
        Assert.Equal(new[] { TownServiceSpecs.Reforge, TownServiceSpecs.WorldStatus }, def.AllServices.Select(s => s.Type));
        Assert.NotNull(def.FindService(TownServiceSpecs.WorldStatus));
        Assert.Null(def.FindService(TownServiceSpecs.Heal));
        using var g = Create();
        var npc = Summon(g, def);
        Assert.Equal(new[] { "shop", "decor" }, g.World.Commerce.Tabs(npc));
        Assert.Equal("torch", Assert.Single(g.World.Commerce.Stock(npc, g.Player)).Item.Id);
        Assert.Equal("gel", Assert.Single(g.World.Commerce.Stock(npc, g.Player, "decor")).Item.Id);
    }

    [Fact]
    public void CurrencyPurchasesSpendTheCurrencyNotCoins()
    {
        var def = new TownNpcDef { Id = "test_keep", Names = new[] { "Bo" }, Shop = new[] { new ShopEntry { Item = "torch", Price = 3, Currency = "gel" } } };
        using var g = Create();
        var npc = Summon(g, def);
        var commerce = g.World.Commerce;
        var inv = g.Player.State.Inventory;
        var torch = g.Content.Items.Get("torch");
        int torches = inv.CountAll(torch);
        long coins = Coins.Total(inv);
        Assert.Equal(TradeResult.TooPoor, commerce.Buy(npc, g.Player, torch));
        inv.Backpack.Add(new ItemStack(g.Content.Items.Get("gel"), 5));
        Assert.Equal(TradeResult.Ok, commerce.Buy(npc, g.Player, torch));
        Assert.Equal(2, inv.Backpack.Count(g.Content.Items.Get("gel")));
        Assert.Equal(torches + 1, inv.CountAll(torch));
        Assert.Equal(coins, Coins.Total(inv));
    }

    private static long NurseCost(TestGame g)
    {
        var nurse = Summon(g, g.Content.TownNpcs.Get("nurse"));
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife - 50;
        return g.World.Commerce.HealCost(nurse, g.Player);
    }

    [Fact]
    public void TheNursesPriceRisesWithProgressAndInExpert()
    {
        using var g = Create();
        long early = NurseCost(g);
        g.World.Progression.Set("defeated_skeletron");
        long later = g.World.Commerce.HealCost(g.World.Town.Find("nurse")!, g.Player);
        Assert.InRange(later / (float)early, 24.5f, 25.5f); // rounding of a small bill
        using var expert = Create(Difficulty.Expert);
        Assert.InRange(NurseCost(expert) / (float)early, 1.9f, 2.1f);
    }

    [Fact]
    public void WorldStatusIsSampledOverTicksAndCached()
    {
        using var g = Create();
        var dryad = Summon(g, g.Content.TownNpcs.Get("dryad"));
        Assert.Null(g.World.Services.WorldStatusText(dryad));
        for (int i = 0; i < 400 && g.World.Town.Status.Sampling; i++) g.World.Town.Tick();
        var report = g.World.Town.Status.Latest!;
        Assert.Equal(100, report.Pure + report.Evil + report.Hallow);
        var text = g.World.Services.WorldStatusText(dryad);
        Assert.Contains("% pure", text);
        Assert.False(g.World.Town.Status.Sampling); // the fresh report is reused
    }
}
