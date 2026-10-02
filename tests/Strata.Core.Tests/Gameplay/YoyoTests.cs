using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Yoyos on a string (P3-12): out while held, within range of the eye, striking repeatedly, home when released.</summary>
public class YoyoTests
{
    private static (TestGame G, Projectile Yoyo) Throw(string item = "wood_yoyo")
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold(item);
        g.Player.Aim = Vector3.UnitZ;
        g.Player.AimDistance = 60f; // the crosshair far off: the string holds it back
        g.Player.UseHeld = true;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        return (g, g.World.Projectiles.TetherOf(g.Player)!);
    }

    [Fact]
    public void AHeldYoyoStaysOutWithinItsRange()
    {
        var (g, yoyo) = Throw();
        using var _ = g;
        var tether = yoyo.Def.Tether!;
        float range = tether.Range * Units.CellSize;
        for (int t = 0; t < tether.Lifetime - 5; t++)
        {
            g.World.Tick();
            Assert.Contains(yoyo, g.World.Projectiles.All);
            Assert.True(Vector3.Distance(yoyo.Position, g.Player.Eye) <= range + 0.01f, $"tick {t}: past the string's reach");
        }
        Assert.False(yoyo.Returning);
        g.Player.ResetUse();
        Assert.NotEqual(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true)); // one yoyo at a time
        Assert.Single(g.World.Projectiles.All);
    }

    [Fact]
    public void ReleasedItComesHomeAndItStrikesAgainAndAgain()
    {
        var (g, yoyo) = Throw();
        using var _ = g;
        var gel = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(0, 0.05f, 2.5f))!;
        gel.Life = gel.MaxLife = 5000;
        gel.Timer = -100_000;
        g.Player.Aim = Vector3.Normalize(gel.Center - g.Player.Eye);
        g.Player.AimDistance = Vector3.Distance(gel.Center, g.Player.Eye);
        int hits = 0, last = gel.Life;
        for (int t = 0; t < 90; t++)
        {
            g.World.Tick();
            if (gel.Life < last) hits++;
            last = gel.Life;
        }
        Assert.True(hits >= 3, $"only {hits} strikes in 1.5 s");
        g.Player.UseHeld = false;
        for (int t = 0; t < 120 && g.World.Projectiles.All.Count > 0; t++) g.World.Tick();
        Assert.Empty(g.World.Projectiles.All);
    }

    [Fact]
    public void ItReturnsWhenItsTimeRunsOut()
    {
        var (g, yoyo) = Throw();
        using var _ = g;
        for (int t = 0; t < yoyo.Def.Tether!.Lifetime + 2; t++) g.World.Tick();
        Assert.True(yoyo.Returning || !g.World.Projectiles.All.Contains(yoyo));
    }
}
