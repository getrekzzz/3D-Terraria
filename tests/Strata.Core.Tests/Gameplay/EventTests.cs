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
        g.World.Spawner.Config = new SpawnConfig { SafeRadius = 0 }; // the arena is the world spawn
        for (int i = 0; i < 40; i++)
            foreach (var n in g.World.Spawner.TrySpawnNear(g.Player)) seen.Add(n.Def.Id);
        Assert.True(seen.Contains("blood_zombie") || seen.Contains("drippler"), "no blood moon creatures: " + string.Join(",", seen));

        g.World.Session.Clock.SetHour(GameClock.DawnHour + 0.01f);
        g.Ticks(2);
        Assert.False(g.World.Events.IsActive("blood_moon"));
    }

    [Fact]
    public void NightEventsRollAtDuskWithTheirChance()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 40; // the Blood Moon needs a player with more than 120 max life
        g.Player.State.Recompute();
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
        var gel = g.Content.Enemies.Get("green_slime");
        for (int i = 0; i < 150; i++)
        {
            var npc = g.World.Npcs.Spawn(gel, g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!;
            g.World.Npcs.Kill(npc);
        }
        Assert.False(g.World.Events.IsActive("slime_rain"));
    }

    [Fact]
    public void ProgressFollowsKills()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        Assert.Null(g.World.Events.LatestInvasion);
        Assert.True(g.World.Events.Start(g.Content.Events.Get("blood_moon"))); // no kill goal: no progress bar
        Assert.Null(g.World.Events.LatestInvasion);
        Assert.True(g.World.Events.Start(g.Content.Events.Get("goblin_army")));
        var army = g.World.Events.LatestInvasion!;
        Assert.Equal("goblin_army", army.Def.Id);
        Assert.Equal(0f, g.World.Events.Progress(army));
        var peon = g.Content.Enemies.Get("goblin_peon");
        for (int i = 0; i < 20; i++)
            g.World.Npcs.Kill(g.World.Npcs.Spawn(peon, g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!);
        Assert.Equal(20f / army.Def.KillGoal, g.World.Events.Progress(army), 4);
        g.World.Npcs.Kill(g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!);
        Assert.Equal(20, army.Kills); // only goblins count
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

    [Fact]
    public void EvilPresenceCallsTheEyeAfterItsCountdown()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Session.Clock.SetHour(20f);
        g.Ticks(2);
        var messages = new List<string>();
        g.World.Events.Announced += (m, _) => messages.Add(m);
        Assert.True(g.World.Events.Start(g.Content.Events.Get("evil_presence")));
        g.Ticks(590);
        Assert.Equal(0, g.World.Npcs.All.Count(n => n.Def.Id == "eye_of_cthulhu"));
        g.Ticks(20);
        Assert.Equal(1, g.World.Npcs.All.Count(n => n.Def.Id == "eye_of_cthulhu"));
        Assert.Equal(new[] { "You feel an evil presence watching you..." }, messages);
        Assert.False(g.World.Events.IsActive("evil_presence"));
    }

    [Fact]
    public void EvilPresenceNeedsLifeDefenseAndAnUndefeatedEye()
    {
        using var g = TestGame.Create();
        var cond = Strata.Core.Conditions.ConditionCompiler.CompileAll(g.Content.Events.Get("evil_presence").Conditions);
        var ctx = new Strata.Core.Conditions.ConditionContext { PlayerMaxLife = 200, PlayerDefense = 10, Flags = new HashSet<string>() };
        Assert.True(cond.Evaluate(ctx));
        ctx.PlayerDefense = 9;
        Assert.False(cond.Evaluate(ctx));
        ctx.PlayerDefense = 10;
        ctx.PlayerMaxLife = 180;
        Assert.False(cond.Evaluate(ctx));
        ctx.PlayerMaxLife = 400;
        ctx.Flags = new HashSet<string> { "defeated_eye_of_cthulhu" };
        Assert.False(cond.Evaluate(ctx));
    }

    [Theory]
    [InlineData(false, 150)]
    [InlineData(true, 75)]
    public void SlimeRainEndsWithKingSlime(bool beaten, int goal)
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        if (beaten) g.World.Progression.Set("defeated_king_slime");
        Assert.True(g.World.Events.Start(g.Content.Events.Get("slime_rain")));
        var gel = g.Content.Enemies.Get("green_slime");
        for (int i = 0; i < goal; i++)
        {
            Assert.Equal(0, g.World.Npcs.All.Count(n => n.Def.Id == "king_slime"));
            var npc = g.World.Npcs.Spawn(gel, g.Player.Position + new System.Numerics.Vector3(3, 0, 3))!;
            g.World.Npcs.Kill(npc);
        }
        Assert.False(g.World.Events.IsActive("slime_rain"));
        Assert.Equal(1, g.World.Npcs.All.Count(n => n.Def.Id == "king_slime"));
    }

    [Fact]
    public void OnlyOneMechanicalWarningPerNight()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Progression.Set("hardmode");
        g.World.Progression.Set("smashed_altar_1");
        g.World.Events.Roll(Strata.Core.Content.Defs.EventPhase.Night, chance: 1f);
        Assert.Equal(1, g.World.Events.Active.Count(a => a.Def.Group == "mech_night"));
        // Beaten bosses send no warning.
        foreach (var a in g.World.Events.Active.ToList()) g.World.Events.Stop(a.Def.Id);
        g.World.Progression.Set("defeated_the_twins");
        g.World.Progression.Set("defeated_the_destroyer");
        g.World.Events.Roll(Strata.Core.Content.Defs.EventPhase.Night, chance: 1f);
        Assert.Equal("mech_prime_night", Assert.Single(g.World.Events.Active, a => a.Def.Group == "mech_night").Def.Id);
    }
}
