using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class MechBossTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        g.World.Session.Clock.SetHour(22f);
        return g;
    }

    private static Npc Boss(TestGame g, string id, Vector3 offset) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + offset)!;

    [Fact]
    public void TheTwinLensesArriveTogetherAndFallTogether()
    {
        using var g = Arena();
        int awoken = 0, defeated = 0;
        g.World.Npcs.BossSpawned += _ => awoken++;
        g.World.Npcs.BossDefeated += _ => defeated++;
        g.Hold("mechanical_eye", 1);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        var ray = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "retinazer");
        var flare = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "spazmatism");
        Assert.Equal(1, awoken);

        g.World.Npcs.Kill(ray);
        Assert.False(g.World.Progression.IsSet("defeated_the_twins"), "one eye down is not the pair");
        Assert.Equal(0, defeated);
        g.World.Npcs.Kill(flare);
        Assert.True(g.World.Progression.IsSet("defeated_the_twins"));
        Assert.Equal(1, defeated);
    }

    [Fact]
    public void ClockworkSummonsWaitForTheNight()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(12f);
        foreach (var summon in new[] { "mechanical_eye", "mechanical_worm", "mechanical_skull" })
        {
            g.Hold(summon, 1);
            Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        }
        Assert.DoesNotContain(g.World.Npcs.All, n => n.IsBoss);
    }

    [Fact]
    public void AWoundedEyeOpensAndFiresFaster()
    {
        using var g = Arena();
        var ray = Boss(g, "retinazer", new Vector3(0, 5f, 10f));
        g.World.Npcs.Kill(g.World.Npcs.All.First(n => n.Def.Id == "spazmatism"));
        var counted = new HashSet<long>();
        int Volleys(int ticks)
        {
            int fired = 0;
            for (int i = 0; i < ticks; i++)
            {
                g.Ticks(1);
                foreach (var p in g.World.Projectiles.All)
                    if (p.Def.Id == "death_laser" && counted.Add(p.Id)) fired++;
            }
            return fired;
        }
        int calm = Volleys(300);
        Assert.False(ray.Enraged);
        ray.Life = ray.MaxLife / 3;
        g.Ticks(2);
        Assert.True(ray.Enraged);
        Assert.Equal("retinazer_open", ray.ModelOverride);
        int wild = Volleys(300);
        Assert.True(wild > calm * 2, $"enraged {wild} vs calm {calm} volleys");
    }

    [Fact]
    public void TheIronSerpentReleasesACappedSwarmOfProbes()
    {
        using var g = Arena();
        var serpent = Boss(g, "the_destroyer", new Vector3(0, 6f, 12f));
        Assert.Equal(50, serpent.Segments.Count);
        g.Ticks(200 * 5);
        int probes = g.World.Npcs.All.Count(n => n.Def.Id == "probe" && n.Alive);
        Assert.InRange(probes, 1, 6);
        g.Ticks(200 * 4);
        Assert.InRange(g.World.Npcs.All.Count(n => n.Def.Id == "probe" && n.Alive), 1, 6);
    }

    [Fact]
    public void AllThreeMechanicalTerrorsMoveTheWorldOn()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_wall_of_flesh");
        foreach (var flag in new[] { "defeated_the_twins", "defeated_the_destroyer" }) g.World.Progression.Set(flag);
        Assert.False(g.World.Progression.IsSet("defeated_all_mechs"));
        g.World.Progression.Set("defeated_skeletron_prime");
        Assert.True(g.World.Progression.IsSet("defeated_all_mechs"));
    }
}
