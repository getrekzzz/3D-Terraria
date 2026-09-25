using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Events;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class EventTests
{
    [Fact]
    public void BloodMoonChangesSpawnsAndEndsAtDawn()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Session.Clock.SetHour(22f);
        g.Ticks(2);
        var def = g.Content.Events.Get("blood_moon");
        Assert.True(g.World.Events.Start(def));
        Assert.Contains("blood_moon", g.World.Session.ContextAt(g.Floor).Events);
        Assert.True(g.World.Events.SpawnRateMultiplier > 2f);

        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
            foreach (var n in g.World.Spawner.TrySpawnNear(g.Player)) seen.Add(n.Def.Id);
        Assert.True(seen.Contains("gore_shambler") || seen.Contains("drip_eye"), "no blood moon creatures: " + string.Join(",", seen));

        g.World.Session.Clock.SetHour(GameClock.DawnHour + 0.01f);
        g.Ticks(2);
        Assert.False(g.World.Events.IsActive("blood_moon"));
    }

    [Fact]
    public void NightEventsRollAtDuskWithTheirChance()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        int started = 0;
        g.World.Events.Started += e => { if (e.Id == "blood_moon") started++; };
        var clock = g.World.Session.Clock;
        for (int night = 0; night < 120; night++)
        {
            clock.SetHour(GameClock.DuskHour - 0.01f);
            g.Ticks(2);
            clock.SetHour(GameClock.DuskHour + 0.01f);
            g.Ticks(2);
            clock.SetHour(GameClock.DawnHour + 0.01f);
            g.Ticks(2);
            clock.Advance(GameClock.TicksPerDay); // next day (moon phase advances)
        }
        Assert.InRange(started, 2, 30);
    }

    [Fact]
    public void KillGoalsEndEvents()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        Assert.True(g.World.Events.Start(g.Content.Events.Get("slime_rain")));
        var gel = g.Content.Enemies.Get("green_gel");
        for (int i = 0; i < 60; i++)
        {
            var npc = g.World.Npcs.Spawn(gel, g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!;
            g.World.Npcs.Kill(npc);
        }
        Assert.False(g.World.Events.IsActive("slime_rain"));
    }

    [Fact]
    public void ActiveEventsPersistInWorldMeta()
    {
        using var g = TestGame.Create();
        g.World.Events.Start(g.Content.Events.Get("slime_rain"));
        g.World.Events.Persist();
        var saved = Assert.Single(g.World.Session.Meta.EventStates);
        Assert.Equal("slime_rain", saved.Id);
        g.World.Events.Stop("slime_rain");
        var restored = new EventDirector(g.World, g.World.Npcs);
        Assert.True(restored.IsActive("slime_rain"));
    }
}
