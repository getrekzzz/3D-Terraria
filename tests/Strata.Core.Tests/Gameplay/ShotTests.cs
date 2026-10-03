using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Where shots start (P3-09'): the weapon's business end, the sky over the crosshair, or the crosshair for minions; ammo saving.</summary>
public class ShotTests
{
    private static TestGame Armed(string weapon, string? ammo = null, int ammoCount = 20)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold(weapon);
        if (ammo != null) g.Player.State.Inventory.Backpack[30] = new ItemStack(g.Content.Items.Get(ammo), ammoCount);
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.UnitZ;
        g.Player.AimDistance = 8f;
        return g;
    }

    private static List<Projectile> Fire(TestGame g, UseOutcome expected)
    {
        var shots = new List<Projectile>();
        g.World.Projectiles.Spawned += p => shots.Add(p);
        g.Player.ResetUse();
        Assert.Equal(expected, g.World.ItemUse.Use(g.Player, null, true));
        return shots;
    }

    [Fact]
    public void GunsFireFromTheMuzzleAheadOfTheHand()
    {
        using var g = Armed("flintlock_pistol", "musket_ball");
        var shot = Assert.Single(Fire(g, UseOutcome.Shot));
        var eye = g.Player.Eye;
        Assert.True(shot.SpawnOrigin.Z - eye.Z > 0.45f, $"{shot.SpawnOrigin.Z - eye.Z:0.00} m ahead of the eye");
        Assert.True(eye.X - shot.SpawnOrigin.X > 0.15f, "to the right of the eye (−X when looking along +Z)");
    }

    [Fact]
    public void StarfurySwingsAndRainsStarsOntoTheCrosshair()
    {
        using var g = Armed("starfury");
        var stars = Fire(g, UseOutcome.Shot); // a sword that shoots swings too (P1-16)
        var star = Assert.Single(stars);
        var target = g.Player.Eye + g.Player.Aim * g.Player.AimDistance;
        Assert.True(star.Position.Y > target.Y + 10f, $"starts {star.Position.Y - target.Y:0.0} m above the target");
        // It falls onto the crosshair point: the line from its start passes within a metre of it.
        var dir = Vector3.Normalize(star.Velocity);
        var toTarget = target - star.Position;
        float miss = (toTarget - dir * Vector3.Dot(toTarget, dir)).Length();
        Assert.True(miss < 1f, $"misses the crosshair by {miss:0.00} m");
    }

    [Fact]
    public void MinionsAppearAtTheCrosshair()
    {
        using var g = Armed("slime_staff");
        g.Player.AimDistance = 5f;
        var minion = Assert.Single(Fire(g, UseOutcome.Shot));
        var target = g.Player.Eye + g.Player.Aim * 5f;
        Assert.True(Vector3.Distance(minion.Position, target) < 1.5f, $"{Vector3.Distance(minion.Position, target):0.00} m from the crosshair");
    }

    [Fact]
    public void TheMegasharkSavesAboutHalfItsAmmo()
    {
        using var g = Armed("megashark", "musket_ball", 999);
        int shots = 0;
        g.World.Projectiles.Spawned += _ => shots++;
        for (int i = 0; i < 300; i++)
        {
            g.Player.ResetUse();
            g.World.ItemUse.Use(g.Player, null, true);
            g.Player.UseCooldown = 0;
        }
        int used = 999 - g.Player.State.Inventory.Backpack[30].Count;
        Assert.True(shots >= 300, $"{shots} shots");
        Assert.InRange(used, 110, 190); // 50% ± noise of 300 shots
    }
}
