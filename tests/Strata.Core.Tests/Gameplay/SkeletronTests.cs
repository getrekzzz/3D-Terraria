using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Skeletron: a skull with two separately killable hands that slap and swipe in turn, spins, Expert skulls, day rage.</summary>
public class SkeletronTests
{
    private const string Defeated = "defeated_skeletron";

    private static TestGame Arena(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        g.World.Session.Clock.SetHour(22f);
        // Open sky over the arena so the skull always sees the player.
        var session = g.World.Session;
        var batch = session.World.BeginEdit();
        for (int dz = -TestGame.ArenaRadius; dz <= TestGame.ArenaRadius; dz++)
            for (int dx = -TestGame.ArenaRadius; dx <= TestGame.ArenaRadius; dx++)
                for (int dy = TestGame.ArenaHeight + 1; dy <= 40; dy++)
                    batch.SetBlock(g.Floor.X + dx, g.Floor.Y + dy, g.Floor.Z + dz, 0);
        session.Commit(batch);
        return g;
    }

    private static Npc Summon(TestGame g) =>
        g.World.Npcs.Spawn(g.Content.Enemies.Get("skeletron"), g.Player.Position + new Vector3(0, 6f, 4f))!;

    private static List<Npc> Hands(Npc head) => head.Followers.Where(f => f.IsPart && f.Alive).ToList();

    /// <summary>One tick with the player held in place at full life.</summary>
    private static void Step(TestGame g, Vector3 stand)
    {
        g.World.Tick();
        g.Player.Position = stand;
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
    }

    [Fact]
    public void ArrivesWithTwoKillableHands()
    {
        using var g = Arena();
        var head = Summon(g);
        var hands = Hands(head);
        Assert.Equal(2, hands.Count);
        Assert.All(hands, h => Assert.Equal("skeletron_hand", h.Def.Id));
        Assert.All(hands, h => Assert.Equal(600, h.MaxLife));
        Assert.All(hands, h => Assert.False(h.InBossBar)); // the boss bar shows the skull's life only
        Assert.Single(hands, h => h.Mirrored && h.LeaderOffset.X < 0); // the left hand is mirrored
        Assert.Equal(4400, head.MaxLife);
        Assert.Equal(10, head.Def.Defense);

        Assert.True(g.World.Npcs.Hit(hands[0], g.Player, 100_000, DamageClass.Melee, 0, 0f, g.Player.Center, melee: true));
        Assert.False(hands[0].Alive);
        Assert.True(head.Alive);
        Assert.False(g.World.Progression.IsSet(Defeated));
    }

    [Fact]
    public void HandsSlapAndSwipeInTurn()
    {
        using var g = Arena();
        var head = Summon(g);
        var hands = Hands(head);
        var left = hands.Single(h => h.LeaderOffset.X < 0);
        var right = hands.Single(h => h.LeaderOffset.X > 0);
        var stand = g.Player.Position;
        int leftStrike = -1, rightStrike = -1;
        float leftClosest = float.MaxValue, rightClosest = float.MaxValue;
        Vector3 leftVel = default, rightVel = default;
        for (int t = 0; t < 700; t++)
        {
            Step(g, stand);
            Assert.Equal(0, head.Phase); // no spin in the first 800 ticks
            if (left.Phase == 2 && leftStrike < 0) (leftStrike, leftVel) = (t, left.Direction);
            if (right.Phase == 2 && rightStrike < 0) (rightStrike, rightVel) = (t, right.Direction);
            if (leftStrike >= 0 && right.Phase == 0 && rightStrike < 0) leftClosest = MathF.Min(leftClosest, Vector3.Distance(left.Center, g.Player.Center));
            if (rightStrike >= 0) rightClosest = MathF.Min(rightClosest, Vector3.Distance(right.Center, g.Player.Center));
        }
        Assert.True(leftStrike >= 0 && rightStrike >= 0, $"strikes at {leftStrike} / {rightStrike}");
        Assert.True(leftStrike < rightStrike, "the left hand strikes first");
        Assert.InRange(rightStrike - leftStrike, 130, 170);
        Assert.True(leftClosest < 1.5f, $"left hand came within {leftClosest:0.0} m");
        Assert.True(rightClosest < 1.5f, $"right hand came within {rightClosest:0.0} m");
        // The left slaps down from above; the right swipes sideways.
        float Horizontal(Vector3 v) => new Vector2(v.X, v.Z).Length();
        Assert.True(-leftVel.Y > Horizontal(leftVel) * 2f, $"slap velocity {leftVel}");
        Assert.True(MathF.Abs(rightVel.Y) < Horizontal(rightVel) * 0.5f, $"swipe velocity {rightVel}");
    }

    [Fact]
    public void KillingAHandLeavesTheSkullFighting()
    {
        using var g = Arena();
        var head = Summon(g);
        var hands = Hands(head);
        g.World.Npcs.Kill(hands[0]);
        var stand = g.Player.Position;
        bool struck = false;
        for (int t = 0; t < 600; t++)
        {
            Step(g, stand);
            struck |= hands[1].Phase == 2;
        }
        Assert.True(head.Alive);
        Assert.Contains(head, g.World.Npcs.All);
        Assert.DoesNotContain(hands[0], g.World.Npcs.All);
        Assert.Equal(1, CryptWardenBrain.HandsAlive(head));
        Assert.True(struck, "the surviving hand kept attacking");
        Assert.True(Vector3.Distance(head.Center, g.Player.Center) < 12f, "the skull kept hunting the player");
        Assert.False(g.World.Progression.IsSet(Defeated));
    }

    [Fact]
    public void KillingTheSkullRemovesTheHandsAndRaisesTheFlagOnce()
    {
        using var g = Arena();
        var head = Summon(g);
        var hands = Hands(head);
        int defeats = 0;
        g.World.Npcs.BossDefeated += _ => defeats++;
        g.Ticks(5);
        g.World.Npcs.Kill(head);
        foreach (var h in hands) g.World.Npcs.Kill(h); // `slay` reaching them afterwards changes nothing
        g.World.Tick();
        Assert.True(g.World.Progression.IsSet(Defeated));
        Assert.Equal(1, defeats);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id is "skeletron" or "skeletron_hand");
    }

    [Fact]
    public void SpinLowersTheSkullsDefense()
    {
        using var g = Arena();
        var head = Summon(g);
        var p = head.Def.Brain.Parse<CryptWardenParams>();
        var stand = g.Player.Position;
        bool spun = false, cued = false;
        g.World.Npcs.Cued += (n, id) => cued |= n == head && id == "boss_roar";
        for (int t = 0; t < p.SpinInterval + 60; t++)
        {
            Step(g, stand);
            if (head.Phase == 1)
            {
                spun = true;
                Assert.Equal(0, head.Defense);
            }
            else Assert.Equal(10, head.Defense);
        }
        Assert.True(spun, "never began a spin");
        Assert.True(cued, "the spin began without a roar");
    }

    [Fact]
    public void ExpertHandsArmourTheSkullAndSkullsFlyOnceOneFalls()
    {
        using var g = Arena(Difficulty.Expert);
        var head = Summon(g);
        var stand = g.Player.Position;
        Step(g, stand);
        Assert.Equal(60, head.Defense);
        for (int t = 0; t < 400; t++)
        {
            Step(g, stand);
            Assert.DoesNotContain(g.World.Projectiles.All, pr => pr.Def.Id == "skeletron_skull");
        }
        g.World.Npcs.Kill(Hands(head)[0]);
        Strata.Core.Gameplay.Entities.Projectile? skull = null;
        for (int t = 0; t < 90 && skull == null; t++)
        {
            Step(g, stand);
            skull = g.World.Projectiles.All.FirstOrDefault(pr => pr.Def.Id == "skeletron_skull");
        }
        Assert.NotNull(skull);
        Assert.Equal(35, head.Defense);
        // It homes: with the player stepped aside, its heading swings towards them.
        stand += new Vector3(3f, 0, 0);
        g.Player.Position = stand;
        float Off() => Vector3.Dot(Vector3.Normalize(skull!.Velocity), Vector3.Normalize(g.Player.Center - skull.Position));
        float before = Off();
        for (int t = 0; t < 20; t++) Step(g, stand);
        Assert.True(Off() > before, $"heading alignment {before:0.00} -> {Off():0.00}");
    }

    [Fact]
    public void DaylightMakesItLethal()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(12f);
        var head = Summon(g);
        var stand = g.Player.Position;
        for (int t = 0; t < 35; t++) Step(g, stand);
        Assert.True(head.Frenzied);
        Assert.Equal(9999, head.Defense);
        Assert.True(g.World.Npcs.ContactBase(head) >= 1000);
        Assert.Equal(1, head.Phase); // it spins without pause
        foreach (var hand in Hands(head))
        {
            Assert.Equal(9999, hand.Defense);
            Assert.True(g.World.Npcs.ContactBase(hand) >= 1000);
        }
        Assert.Contains(head, g.World.Npcs.All); // it does not leave by day
    }

    [Fact]
    public void PrimeRagesByDayWhileTheTwinsStillLeave()
    {
        using var g = Arena();
        var prime = g.Content.Enemies.Get("skeletron_prime").Boss!;
        Assert.False(prime.DespawnAtDay);
        Assert.NotNull(prime.Enrage);
        // Both eyes of the pair fly off at dawn (one staying behind would hold the fight open by day).
        Assert.True(g.Content.Enemies.Get("retinazer").Boss!.DespawnAtDay);
        Assert.True(g.Content.Enemies.Get("spazmatism").Boss!.DespawnAtDay);
    }
}
