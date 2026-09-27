using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class InvasionTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static void Use(TestGame g, string item)
    {
        g.Hold(item);
        g.World.ItemUse.Use(g.Player, null, freshPress: true);
        g.Player.UseCooldown = 0;
    }

    [Fact]
    public void TheMedallionCallsThePumpkinMoonOnlyAtNight()
    {
        using var g = Arena();
        string? refused = null;
        g.World.ItemUse.Refused += (_, m) => refused = m;
        g.World.Session.Clock.SetHour(12);
        Use(g, "pumpkin_moon_medallion");
        Assert.False(g.World.Events.IsActive("pumpkin_moon"));
        Assert.Equal("The medallion only works at night.", refused);
        g.World.Session.Clock.SetHour(22);
        g.World.Tick();
        Use(g, "pumpkin_moon_medallion");
        Assert.True(g.World.Events.IsActive("pumpkin_moon"));
        Assert.Equal(0, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("pumpkin_moon_medallion")));
    }

    [Fact]
    public void LaterWavesAndBossesWaitForTheKillCount()
    {
        using var g = Arena();
        var pumpking = g.Content.SpawnRules.Get("pumpking_pumpkin_moon");
        var cond = ConditionCompiler.CompileAll(pumpking.Conditions);
        g.World.Session.Clock.SetHour(22);
        g.World.Tick();
        g.World.Events.Start(g.Content.Events.Get("pumpkin_moon"));
        var at = g.Floor + Common.Int3.Up;
        Assert.False(cond.Evaluate(g.World.Session.ContextAt(at)));
        var scarecrow = g.Content.Enemies.Get("scarecrow");
        for (int i = 0; i < 40; i++) g.World.Npcs.Kill(g.World.Npcs.Spawn(scarecrow, TestGame.CellFeet(at))!);
        Assert.True(cond.Evaluate(g.World.Session.ContextAt(at)));
    }

    [Fact]
    public void InvasionsMarchOnThroughTheNightUntilBeaten()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(12);
        g.World.Tick();
        g.World.Events.Start(g.Content.Events.Get("pirate_invasion"));
        g.World.Session.Clock.SetHour(GameClock.DuskHour + 0.5f); // night falls
        g.World.Tick();
        Assert.True(g.World.Events.IsActive("pirate_invasion"));
        var deckhand = g.Content.Enemies.Get("pirate_deckhand");
        for (int i = 0; i < 120; i++) g.World.Npcs.Kill(g.World.Npcs.Spawn(deckhand, g.Player.Position + new Vector3(3, 0, 3))!);
        Assert.False(g.World.Events.IsActive("pirate_invasion"));
        Assert.True(g.World.Progression.IsSet("defeated_pirate_invasion"));
    }

    [Fact]
    public void EventLootFeedsTheLegendaryRecipes()
    {
        using var g = Arena();
        var terra = g.Content.Recipes.All.First(r => r.Output == "terra_blade");
        Assert.Contains(terra.Inputs, i => i.Item == "broken_hero_sword");
        var zenith = g.Content.Recipes.All.First(r => r.Output == "zenith");
        Assert.Contains(zenith.Inputs, i => i.Item == "influx_waver");
        Assert.Contains(zenith.Inputs, i => i.Item == "the_horsemans_blade");
        Assert.Contains(g.Content.LootTables.Get("mothron").Entries, e => e.Item == "broken_hero_sword");
        Assert.Contains(g.Content.LootTables.Get("pumpking").Entries, e => e.Item == "the_horsemans_blade");
        Assert.Contains(g.Content.LootTables.Get("martian_saucer").Entries, e => e.Item == "influx_waver");
    }
}
