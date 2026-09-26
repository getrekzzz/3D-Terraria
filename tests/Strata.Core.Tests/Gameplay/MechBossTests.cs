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
        g.Hold("clockwork_eye", 1);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        var ray = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "twin_lens_ray");
        var flare = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "twin_lens_flare");
        Assert.Equal(1, awoken);

        g.World.Npcs.Kill(ray);
        Assert.False(g.World.Progression.IsSet("defeated_mech_twins"), "one eye down is not the pair");
        Assert.Equal(0, defeated);
        g.World.Npcs.Kill(flare);
        Assert.True(g.World.Progression.IsSet("defeated_mech_twins"));
        Assert.Equal(1, defeated);
    }

    [Fact]
    public void ClockworkSummonsWaitForTheNight()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(12f);
        foreach (var summon in new[] { "clockwork_eye", "clockwork_worm", "clockwork_skull" })
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
        var ray = Boss(g, "twin_lens_ray", new Vector3(0, 5f, 10f));
        g.World.Npcs.Kill(g.World.Npcs.All.First(n => n.Def.Id == "twin_lens_flare"));
        var counted = new HashSet<long>();
        int Volleys(int ticks)
        {
            int fired = 0;
            for (int i = 0; i < ticks; i++)
            {
                g.Ticks(1);
                foreach (var p in g.World.Projectiles.All)
                    if (p.Def.Id == "mech_laser" && counted.Add(p.Id)) fired++;
            }
            return fired;
        }
        int calm = Volleys(300);
        Assert.False(ray.Enraged);
        ray.Life = ray.MaxLife / 3;
        g.Ticks(2);
        Assert.True(ray.Enraged);
        Assert.Equal("twin_lens_ray_open", ray.ModelOverride);
        int wild = Volleys(300);
        Assert.True(wild > calm * 2, $"enraged {wild} vs calm {calm} volleys");
    }

    [Fact]
    public void TheIronSerpentReleasesACappedSwarmOfProbes()
    {
        using var g = Arena();
        var serpent = Boss(g, "iron_serpent", new Vector3(0, 6f, 12f));
        Assert.Equal(50, serpent.Segments.Count);
        g.Ticks(200 * 5);
        int probes = g.World.Npcs.All.Count(n => n.Def.Id == "serpent_probe" && n.Alive);
        Assert.InRange(probes, 1, 6);
        g.Ticks(200 * 4);
        Assert.InRange(g.World.Npcs.All.Count(n => n.Def.Id == "serpent_probe" && n.Alive), 1, 6);
    }

    [Fact]
    public void WardenPrimeHoldsFourToolsAndOnlyItsGunsFire()
    {
        using var g = Arena();
        var prime = Boss(g, "warden_prime", new Vector3(0, 7f, 8f));
        Assert.Equal(4, prime.Segments.Count);
        var fromParts = new List<int>();
        var seen = new HashSet<long>();
        for (int i = 0; i < 600; i++)
        {
            g.Ticks(1);
            foreach (var p in g.World.Projectiles.All)
                if (p.Owner == null && seen.Add(p.Id))
                {
                    // The part each shot left from.
                    var start = p.Position - p.Velocity / Strata.Core.Common.Units.TicksPerSecond;
                    int nearest = Enumerable.Range(0, 4).OrderBy(k => Vector3.DistanceSquared(prime.Segments[k], start)).First();
                    fromParts.Add(nearest);
                }
        }
        Assert.NotEmpty(fromParts);
        Assert.All(fromParts, k => Assert.Contains(k, new[] { 2, 3 }));
        // The guns (second pair) hang well below the skull, one either side (the saw and vice swipe, so they move).
        Assert.True(prime.Segments[2].Y < prime.Center.Y - 1.8f && prime.Segments[3].Y < prime.Center.Y - 1.8f,
            $"guns at {prime.Segments[2].Y:F1}/{prime.Segments[3].Y:F1}, skull at {prime.Center.Y:F1}");
        Assert.True(Vector3.Distance(prime.Segments[2], prime.Segments[3]) > 4f);
    }

    [Fact]
    public void AllThreeMechanicalTerrorsMoveTheWorldOn()
    {
        using var g = Arena();
        g.World.Progression.Set("defeated_mawwall");
        foreach (var flag in new[] { "defeated_mech_twins", "defeated_mech_worm" }) g.World.Progression.Set(flag);
        Assert.False(g.World.Progression.IsSet("defeated_all_mechs"));
        g.World.Progression.Set("defeated_mech_warden");
        Assert.True(g.World.Progression.IsSet("defeated_all_mechs"));
    }
}
