using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-19: visiting merchants come at dawn, leave at dusk, rotate their stock and never count as residents.</summary>
public class VisitorTests
{
    private static readonly Int3 West = new(-7, 0, 0), East = new(7, 0, 0);

    private static TestGame CreateTown()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        TestHouses.Build(g, g.Floor + West);
        TestHouses.Build(g, g.Floor + East);
        var town = g.World.Town;
        foreach (var (id, house) in new[] { ("guide", West), ("merchant", East) })
        {
            var npc = town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + new Int3(0, 1, -9), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
            Assert.Equal(AssignResult.Assigned, town.AssignHome(npc, g.Floor + house + new Int3(-2, 2, -2), out _));
        }
        return g;
    }

    private static void Visits(TestGame g, int checks)
    {
        for (int i = 0; i < checks; i++) g.World.Town.UpdateVisitors();
    }

    [Fact]
    public void TravellingMerchantArrivesAtDawnAndLeavesAtDusk()
    {
        using var g = CreateTown();
        var town = g.World.Town;
        var clock = g.World.Session.Clock;
        town.DawnChanceOverride = 1f;
        town.ArrivalsEnabled = true;
        int arrived = 0, left = 0;
        town.VisitorArrived += _ => arrived++;
        town.VisitorLeft += _ => left++;
        clock.SetHour(3f);
        clock.Advance(GameClock.TicksPerDay);
        Visits(g, 1);
        Assert.Null(town.Find("travelling_merchant")); // still night
        clock.SetHour(5f);
        Visits(g, 1);
        var tm = town.Find("travelling_merchant");
        Assert.NotNull(tm);
        Assert.Equal(1, arrived);
        Assert.Equal(clock.Day, tm!.VisitDay);
        Visits(g, 3);
        Assert.Equal(1, arrived); // one roll a day
        clock.SetHour(19.6f);
        Visits(g, 200);
        Assert.Null(town.Find("travelling_merchant"));
        Assert.Equal(1, left);
    }

    [Fact]
    public void NoVisitWithoutTwoResidents()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var town = g.World.Town;
        town.DawnChanceOverride = 1f;
        g.World.Session.Clock.SetHour(6f);
        Visits(g, 2);
        Assert.Null(town.Find("travelling_merchant"));
    }

    [Fact]
    public void StockIsStablePerVisitAndRotates()
    {
        using var g = CreateTown();
        var town = g.World.Town;
        var def = g.Content.TownNpcs.Get("travelling_merchant");
        var tm = town.ArriveVisitor(def)!;
        Assert.NotNull(tm);
        var commerce = g.World.Commerce;
        string Ids() => string.Join(",", commerce.Stock(tm, g.Player).Select(o => o.Item.Id));
        var first = Ids();
        Assert.Equal(first, Ids());
        Assert.Equal(def.Visit!.Pools.Length, commerce.VisitorStock(tm).Count);
        var seen = new HashSet<string> { first };
        for (int day = 1; day <= 12; day++)
        {
            tm.VisitDay = tm.VisitDay + 1;
            seen.Add(Ids());
        }
        Assert.True(seen.Count > 1, "the stock never changed between visits");
    }

    [Fact]
    public void SkeletonMerchantComesOnlyUnderground()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var def = g.Content.TownNpcs.Get("skeleton_merchant");
        Assert.Equal(TownNpcKind.Visitor, def.Kind);
        Assert.Equal(VisitInfo.SiteMode, def.Visit!.Mode);
        var where = ConditionCompiler.CompileAll(def.Visit.Where);
        var ctx = new ConditionContext { Layer = Strata.Core.World.WorldLayer.Surface };
        Assert.False(where.Evaluate(ctx));
        ctx.Layer = Strata.Core.World.WorldLayer.Cavern;
        Assert.True(where.Evaluate(ctx));
        // The arena is on the surface: hourly rolls never place him there.
        for (int i = 0; i < 200; i++) g.World.Town.UpdateVisitors();
        Assert.Null(g.World.Town.Find("skeleton_merchant"));
    }

    [Fact]
    public void VisitorsDoNotCountOrTakeHomes()
    {
        using var g = CreateTown();
        var town = g.World.Town;
        var tm = town.ArriveVisitor(g.Content.TownNpcs.Get("travelling_merchant"))!;
        Assert.False(tm.CountsAsResident);
        Assert.False(tm.NeedsHome);
        Assert.Null(tm.Home);
        Assert.Equal(2, town.TownContext(g.Player).ResidentCount);
        Assert.Equal(1f, g.World.Commerce.Happiness(tm).Factor);
        Assert.True(town.SendAway(tm));
        Assert.True(tm.Leaving);
    }
}
