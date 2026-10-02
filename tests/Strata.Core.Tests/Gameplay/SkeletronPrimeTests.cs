using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P4-28: Skeletron Prime's saw, vice, cannon and laser are boss parts you can destroy one by one.</summary>
public class SkeletronPrimeTests
{
    private static readonly string[] Arms = { "prime_saw", "prime_vice", "prime_cannon", "prime_laser" };

    private static (TestGame g, Npc prime) Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        g.World.Session.Clock.SetHour(22f);
        var prime = g.World.Npcs.Spawn(g.Content.Enemies.Get("skeletron_prime"), g.Player.Position + new Vector3(0, 7f, 8f))!;
        return (g, prime);
    }

    private static Npc Arm(Npc prime, string id) => prime.Followers.Single(f => f.Def.Id == id);

    private static void Heal(TestGame g) => g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;

    [Fact]
    public void PrimeArmsAreSeparatelyKillable()
    {
        var (g, prime) = Arena();
        using var scope = g;
        Assert.Empty(prime.Segments);
        Assert.Equal((int)(28000 * g.World.Npcs.Scale), prime.MaxLife);
        var life = new Dictionary<string, int> { ["prime_saw"] = 9000, ["prime_vice"] = 9000, ["prime_cannon"] = 7000, ["prime_laser"] = 6000 };
        foreach (var id in Arms)
        {
            var arm = Arm(prime, id);
            Assert.True(arm.IsPart);
            Assert.Equal((int)(life[id] * g.World.Npcs.Scale), arm.MaxLife);
        }
        g.World.Npcs.Kill(Arm(prime, "prime_saw"));
        g.Ticks(2);
        Assert.True(prime.Alive);
        Assert.False(g.World.Progression.IsSet("defeated_skeletron_prime"));
        Assert.Equal(3, prime.Followers.Count(f => f.Alive && f.IsPart));
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "prime_saw");
    }

    [Fact]
    public void CannonAndLaserArmsFireFromTheirArms()
    {
        var (g, prime) = Arena();
        using var scope = g;
        var seen = new HashSet<long>();
        var shooters = new Dictionary<string, string>(); // projectile id -> nearest arm
        var bad = new List<string>();
        for (int i = 0; i < 600; i++)
        {
            g.World.Tick();
            Heal(g);
            foreach (var p in g.World.Projectiles.All)
            {
                if (p.Owner != null || !seen.Add(p.Id)) continue;
                var start = p.Position - p.Velocity / Strata.Core.Common.Units.TicksPerSecond;
                var nearest = Arms.Where(a => prime.Followers.Any(f => f.Def.Id == a)).OrderBy(a => Vector3.DistanceSquared(Arm(prime, a).Center, start)).First();
                if ((p.Def.Id, nearest) is not (("prime_bomb", "prime_cannon") or ("death_laser", "prime_laser"))) bad.Add($"{p.Def.Id} from {nearest}");
                shooters[p.Def.Id] = nearest;
            }
        }
        Assert.Empty(bad);
        Assert.Equal("prime_cannon", shooters["prime_bomb"]);
        Assert.Equal("prime_laser", shooters["death_laser"]);
    }

    [Fact]
    public void TheSawAndTheViceReachTheTarget()
    {
        var (g, prime) = Arena();
        using var scope = g;
        float saw = float.MaxValue, vice = float.MaxValue;
        for (int i = 0; i < 500; i++)
        {
            g.World.Tick();
            Heal(g);
            if (prime.Phase == 1) continue; // spinning arms hold still
            saw = MathF.Min(saw, Vector3.Distance(Arm(prime, "prime_saw").Center, g.Player.Center));
            vice = MathF.Min(vice, Vector3.Distance(Arm(prime, "prime_vice").Center, g.Player.Center));
        }
        Assert.True(saw < 2.5f, $"saw came within {saw:F1} m");
        Assert.True(vice < 2.5f, $"vice came within {vice:F1} m");
    }

    [Fact]
    public void PrimeSpinDoublesDefense()
    {
        var (g, prime) = Arena();
        using var scope = g;
        g.World.Tick();
        Assert.Equal(0, prime.Phase);
        Assert.Equal(24, prime.Defense);
        for (int i = 0; i < 700 && prime.Phase == 0; i++)
        {
            g.World.Tick();
            Heal(g);
        }
        Assert.Equal(1, prime.Phase);
        Assert.Equal(48, prime.Defense);
    }

    [Fact]
    public void KillingPrimesSkullEndsTheFight()
    {
        var (g, prime) = Arena();
        using var scope = g;
        var arms = prime.Followers.ToList();
        g.World.Npcs.Kill(prime);
        Assert.True(g.World.Progression.IsSet("defeated_skeletron_prime"));
        g.Ticks(2);
        Assert.All(arms, a => Assert.DoesNotContain(a, g.World.Npcs.All));
        Assert.DoesNotContain(g.World.Npcs.All, n => Arms.Contains(n.Def.Id));
    }
}
