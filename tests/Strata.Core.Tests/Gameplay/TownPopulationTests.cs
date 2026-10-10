using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-01: who counts as a resident, day-only move-ins, and the return of the fallen at dawn.</summary>
public class TownPopulationTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Summon(TestGame g, string id, Int3 offset) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + offset, Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    private static ICondition Condition(string json) =>
        ConditionCompiler.Compile(System.Text.Json.JsonSerializer.Deserialize<TypedSpec>(json, ContentJson.Options)!);

    [Fact]
    public void SlainResidentReturnsAtDawn()
    {
        using var g = Create();
        var clock = g.World.Session.Clock;
        var guide = Summon(g, "guide", new Int3(0, 1, -6));
        clock.SetHour(20f);
        g.World.Town.Slay(guide);
        Assert.False(guide.Alive);
        clock.Advance(3 * GameClock.TicksPerHour); // 23:00
        g.Ticks(1);
        Assert.False(guide.Alive);
        clock.Advance(4 * GameClock.TicksPerHour); // 03:00 the next day
        g.Ticks(1);
        Assert.False(guide.Alive);
        clock.Advance(GameClock.TicksPerHour + GameClock.TicksPerHour / 2 + 60); // 04:31
        g.Ticks(1);
        Assert.True(guide.Alive);
        Assert.Equal(-1, guide.DiedAtTick);
    }

    [Fact]
    public void ADeathJustBeforeDawnStillWaitsTheMinimum()
    {
        using var g = Create();
        var clock = g.World.Session.Clock;
        var guide = Summon(g, "guide", new Int3(0, 1, -6));
        clock.SetHour(4.25f);
        g.World.Town.Slay(guide);
        clock.Advance(GameClock.TicksPerHour / 4 + 60); // 04:31: dawn has come, but only 16 minutes have passed
        g.Ticks(1);
        Assert.False(guide.Alive);
        clock.Advance(g.World.Town.Config.MinDeadTicks);
        g.Ticks(1);
        Assert.True(guide.Alive);
    }

    [Fact]
    public void ArrivalsWaitForDaylight()
    {
        using var g = Create();
        var town = g.World.Town;
        g.World.Session.Clock.SetHour(22f);
        TestHouses.Build(g, g.Floor + new Int3(-6, 0, 0));
        TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        Assert.Empty(Wallet.Give(g.Player.State.Inventory, g.Content, 5000));
        town.ArrivalsEnabled = true;
        g.Ticks(town.Config.ArrivalInterval + 1); // the guide is placed and housed, but nobody new comes at night
        Assert.NotNull(town.Find("guide")?.Home);
        Assert.Null(town.Find("merchant"));
        town.ArrivalsEnabled = false;
        Assert.Null(town.CheckArrivals());
        Assert.Equal("merchant", town.CheckArrivals(force: true)?.Def.Id);
    }

    [Fact]
    public void DeadAndLandmarkResidentsDoNotCount()
    {
        using var g = Create();
        var town = g.World.Town;
        TestHouses.Build(g, g.Floor + new Int3(6, 0, 0));
        var guide = Summon(g, "guide", new Int3(0, 1, -6));
        Summon(g, "old_man", new Int3(-3, 1, -6));
        town.CheckArrivals();
        var ctx = town.TownContext(g.Player);
        Assert.Equal(1, ctx.ResidentCount);
        Assert.True(Condition("""{"type":"residents","min":1}""").Evaluate(ctx));
        town.Slay(guide);
        ctx = town.TownContext(g.Player);
        Assert.Equal(0, ctx.ResidentCount);
        Assert.DoesNotContain("old_man", ctx.Residents);
        Assert.DoesNotContain("guide", ctx.Residents);
        Assert.False(Condition("""{"type":"residents","min":1}""").Evaluate(ctx));
    }

    [Fact]
    public void DeathAndBondsSurviveSaving()
    {
        using var g = Create();
        var guide = Summon(g, "guide", new Int3(0, 1, -6));
        var merchant = Summon(g, "merchant", new Int3(2, 1, -6));
        merchant.Bound = true;
        g.World.Town.Slay(guide);
        long died = guide.DiedAtTick;
        g.World.Town.Persist();
        var models = g.World.Session.Meta.Residents;
        var json = System.Text.Json.JsonSerializer.Serialize(models);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<Strata.Core.Persistence.WorldMeta.ResidentModel>>(json)!;
        Assert.Equal(died, back.Single(m => m.Id == "guide").DiedAt);
        Assert.True(back.Single(m => m.Id == "merchant").Bound);
        // An old save without the new fields loads with their defaults.
        var old = System.Text.Json.JsonSerializer.Deserialize<Strata.Core.Persistence.WorldMeta.ResidentModel>(
            """{"Id":"guide","Name":"Andrew","Home":null,"HomeRoom":null,"Position":[0,0,0],"Life":0,"RespawnTicks":900}""")!;
        Assert.False(old.Bound);
        Assert.Equal(-1, old.DiedAt);
    }
}
