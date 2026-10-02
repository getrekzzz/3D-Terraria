using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class MechBossTests
{
    private static TestGame Arena(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
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
        g.Ticks(70); // it spins open first
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
        Assert.Equal(60, serpent.Segments.Count);
        g.Ticks(200 * 5);
        int probes = g.World.Npcs.All.Count(n => n.Def.Id == "probe" && n.Alive);
        Assert.InRange(probes, 1, 6);
        g.Ticks(200 * 4);
        Assert.InRange(g.World.Npcs.All.Count(n => n.Def.Id == "probe" && n.Alive), 1, 6);
    }

    [Fact]
    public void WardenPrimeHoldsFourToolsAndOnlyItsGunsFire()
    {
        using var g = Arena();
        var prime = Boss(g, "skeletron_prime", new Vector3(0, 7f, 8f));
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
        g.World.Progression.Set("defeated_wall_of_flesh");
        foreach (var flag in new[] { "defeated_the_twins", "defeated_the_destroyer" }) g.World.Progression.Set(flag);
        Assert.False(g.World.Progression.IsSet("defeated_all_mechs"));
        g.World.Progression.Set("defeated_skeletron_prime");
        Assert.True(g.World.Progression.IsSet("defeated_all_mechs"));
    }

    [Fact]
    public void DestroyerSegmentsHaveTheirOwnDefense()
    {
        using var g = Arena();
        var worm = Boss(g, "the_destroyer", new Vector3(0, 30f, 12f));
        Assert.Equal(80_000, worm.MaxLife);
        int Hit(int segment)
        {
            int before = worm.Life;
            Assert.True(g.World.Npcs.Hit(worm, null, 20, Strata.Core.Content.Defs.DamageClass.None, 0, 0f, worm.Center, melee: false, segment: segment));
            return before - worm.Life;
        }
        int head = Hit(-1), body = Hit(10), tail = Hit(worm.Segments.Count - 1);
        Assert.True(head >= 17, $"the head is soft ({head})");
        Assert.True(body <= 9 && tail <= 6 && head > body, $"plates {body}, tail {tail} are armoured");
        Assert.Equal(60, worm.Segments.Count); // shared life: no segment breaks off
    }

    [Fact]
    public void EachSegmentLaunchesOneProbeThenStopsFiring()
    {
        using var g = Arena();
        var worm = Boss(g, "the_destroyer", new Vector3(0, 6f, 12f));
        int launched = 0;
        g.World.Npcs.Spawned += n => { if (n.Def.Id == "probe") launched++; };
        for (int round = 0; round < 8; round++)
        {
            g.Ticks(200);
            foreach (var p in g.World.Npcs.All.Where(n => n.Def.Id == "probe").ToList()) g.World.Npcs.Kill(p);
        }
        int spent = Enumerable.Range(0, worm.Segments.Count).Count(i => WormBrain.Spent(worm, i));
        Assert.InRange(launched, 4, 8);
        Assert.Equal(launched, spent); // one probe per plate, each plate once
        Assert.False(WormBrain.Spent(worm, worm.Segments.Count - 1), "the tail carries no probe");

        // Every plate spent: no more probes, and the dark worm stops firing.
        for (int i = 0; i < worm.Segments.Count; i++) worm.SegmentFlags[i] |= WormBrain.ProbeSpent;
        g.Ticks(1);
        launched = 0;
        int lasers = 0;
        var seen = g.World.Projectiles.All.Select(p => p.Id).ToHashSet();
        for (int t = 0; t < 600; t++)
        {
            g.Ticks(1);
            foreach (var p in g.World.Projectiles.All)
                if (p.Def.Id == "death_laser" && seen.Add(p.Id)) lasers++;
        }
        Assert.Equal(0, launched);
        Assert.Equal(0, lasers);
    }

    [Fact]
    public void TwinsTransformAtFortyPercent()
    {
        using var g = Arena();
        var ray = Boss(g, "retinazer", new Vector3(0, 6f, 12f));
        var flare = g.World.Npcs.All.First(n => n.Def.Id == "spazmatism");
        Assert.Equal(20_000, ray.MaxLife);
        Assert.Equal(23_000, flare.MaxLife);
        foreach (var twin in new[] { ray, flare }) twin.Life = (int)(twin.MaxLife * 0.42f);
        g.Ticks(2);
        Assert.All(new[] { ray, flare }, t => Assert.Equal(0, t.Stage));
        foreach (var twin in new[] { ray, flare }) twin.Life = (int)(twin.MaxLife * 0.39f);
        g.Ticks(2);
        Assert.All(new[] { ray, flare }, t => Assert.True(t.Invulnerable, "spinning open"));
        g.Ticks(70);
        Assert.All(new[] { ray, flare }, t => Assert.True(t.Enraged));
        Assert.Equal(20, ray.Defense);
        Assert.Equal(28, flare.Defense);
    }

    [Fact]
    public void ExpertTwinRagesWhenItsSiblingFalls()
    {
        foreach (var (difficulty, speed) in new[] { (Difficulty.Classic, 1f), (Difficulty.Expert, 1.25f) })
        {
            using var g = Arena(difficulty);
            var ray = Boss(g, "retinazer", new Vector3(0, 6f, 12f));
            g.Ticks(2);
            Assert.Equal(1f, ray.SpeedScale);
            g.World.Npcs.Kill(g.World.Npcs.All.First(n => n.Def.Id == "spazmatism"));
            g.Ticks(2);
            Assert.Equal(speed, ray.SpeedScale);
        }
    }
}
