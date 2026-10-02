using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-04: Terraria's move-in rules, checked one NPC at a time against forced move-in checks.</summary>
public class TownArrivalRulesTests
{
    private static TestGame Create(int houses)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        Int3[] spots = { new(-7, 0, -7), new(7, 0, -7), new(-7, 0, 7), new(7, 0, 7) };
        for (int i = 0; i < houses; i++) TestHouses.Build(g, g.Floor + spots[i]);
        return g;
    }

    private static TownNpc Summon(TestGame g, string id) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + Int3.Up, Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    private static void Give(TestGame g, string item) =>
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get(item), 1));

    private static string? Arrives(TestGame g) => g.World.Town.CheckArrivals(force: true)?.Def.Id;

    [Fact]
    public void TheDemolitionistWaitsForTheMerchant()
    {
        using var g = Create(2);
        Give(g, "bomb");
        Assert.Null(Arrives(g));
        Summon(g, "merchant");
        Assert.Equal("demolitionist", Arrives(g));
    }

    [Fact]
    public void TheDryadComesAfterAnyBoss()
    {
        using var g = Create(1);
        Assert.Null(Arrives(g));
        g.World.Progression.Set("defeated_deerclops");
        Assert.Equal("dryad", Arrives(g));
    }

    [Fact]
    public void SantaComesOnlyAtChristmasAndLeavesAfter()
    {
        using var g = Create(1);
        g.World.Session.Seasons.Override = "";
        g.World.Progression.Set("defeated_frost_legion");
        Assert.Null(Arrives(g));
        g.World.Session.Seasons.Override = "christmas";
        Assert.Equal("santa_claus", Arrives(g));
        g.World.Session.Seasons.Override = "";
        g.World.Town.UpdateLandmarks();
        Assert.Null(g.World.Town.Find("santa_claus"));
    }

    [Fact]
    public void ThePrincessNeedsThreeHousedResidents()
    {
        using var g = Create(4);
        Summon(g, "guide");
        Summon(g, "clothier");
        Assert.Null(Arrives(g)); // houses both; two residents are not enough
        Summon(g, "witch_doctor");
        Assert.Equal("princess", Arrives(g));
    }

    [Fact]
    public void RescuedNpcsNeedTheirRescue()
    {
        using var g = Create(1);
        g.World.Progression.Set("defeated_goblin_army"); // the old rule no longer applies
        Assert.Null(Arrives(g));
        g.World.Progression.Set(TownRescue.FlagFor("goblin_tinkerer"));
        Assert.Equal("goblin_tinkerer", Arrives(g));
    }

    [Fact]
    public void TheDyeTraderAsksForADye()
    {
        // The Dye Trader's own basic dyes (P5-16) satisfy his 'hasItem category dye' arrival rule.
        var rule = Assert.Single(Support.TestPaths.Content.TownNpcs.Get("dye_trader").Arrival,
            a => a.Type == "hasItem" && a.Raw.GetProperty("category").GetString() == "dye");
        var cond = Strata.Core.Conditions.ConditionCompiler.Compile(rule);
        using var g = Create(0);
        Assert.False(cond.Evaluate(g.World.Town.TownContext(g.Player)));
        Give(g, "red_dye");
        Assert.True(cond.Evaluate(g.World.Town.TownContext(g.Player)));
    }

    [Fact]
    public void VisitingMerchantsNeverMoveIn()
    {
        using var g = Create(3);
        g.World.Progression.Set("defeated_skeletron");
        for (int i = 0; i < 3; i++) Assert.NotEqual("skeleton_merchant", Arrives(g));
        Assert.Null(g.World.Town.Find("skeleton_merchant"));
        Assert.Null(g.World.Town.Find("travelling_merchant"));
    }

    [Fact]
    public void AllTwentyNineTownNpcsLoadFromTheTownFolder() =>
        Assert.Equal(32, Support.TestPaths.Content.TownNpcs.Count); // 29 plus the three town pets (P5-20)
}
