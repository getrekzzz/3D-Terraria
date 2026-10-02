using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>
/// Real gun bursts (several shots inside one use animation) and reuse delay, as in Terraria 1.4.4: the Clockwork
/// Assault Rifle (use time 4, animation 12, reuse delay 14) fires 3 rounds per burst for one bullet, then pauses.
/// </summary>
public class BurstTests
{
    private const string Rifle = "clockwork_assault_rifle";

    private static TestGame Range(int bullets = 100)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.Aim = Vector3.UnitZ;
        g.Player.State.Inventory.Backpack[20] = new ItemStack(g.Content.Items.Get("musket_ball"), bullets);
        return g;
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks, calling Use first each tick while <paramref name="hold"/> (or only on tick 0),
    /// and returns the tick (counted from the first use) of every shot.
    /// </summary>
    private static List<int> Fire(TestGame g, int ticks, bool hold, Action<int>? each = null)
    {
        var shots = new List<int>();
        int now = 0;
        g.World.ItemUse.Shot += _ => shots.Add(now);
        for (int i = 0; i < ticks; i++)
        {
            now = i;
            if (hold || i == 0) g.World.ItemUse.Use(g.Player, null, freshPress: i == 0);
            each?.Invoke(i);
            now = i + 1;
            g.World.Tick();
        }
        return shots;
    }

    [Fact]
    public void ShippedRifleUsesTerrariaTiming()
    {
        using var g = Range();
        var rifle = g.Content.Items.Get(Rifle);
        Assert.Equal((4, 12, 14, true), (rifle.UseTime, rifle.UseAnimation, rifle.ReuseDelay, rifle.AmmoPerBurst));
        Assert.Equal(1, g.World.Uses[rifle].Shoot!.Count); // one bullet per shot: the burst is spread over time
    }

    [Fact]
    public void OnePressFiresThreeRoundsFourTicksApart()
    {
        using var g = Range();
        g.Hold(Rifle);
        var shots = Fire(g, 40, hold: false);
        Assert.Equal(new[] { 0, 4, 8 }, shots); // the burst finishes after the button is let go
        Assert.Equal(1, g.Player.UseSerial); // one animation for the whole burst
        Assert.Equal(12, g.Player.UseAnimationMax);
    }

    [Fact]
    public void ReuseDelayPausesBetweenBursts()
    {
        using var g = Range();
        g.Hold(Rifle);
        var shots = Fire(g, 60, hold: true, i =>
        {
            if (i == 12) Assert.Equal(14, g.Player.ReuseDelay); // set when the animation ends
        });
        // 12 ticks of animation + 14 of reuse delay: a new burst every 26 ticks.
        Assert.Equal(new[] { 0, 4, 8, 26, 30, 34, 52, 56 }, shots.Take(8));
        Assert.Equal(3, g.Player.UseSerial);
    }

    [Fact]
    public void UseIsRefusedDuringTheReuseDelay()
    {
        using var g = Range();
        g.Hold(Rifle);
        Fire(g, 13, hold: false);
        Assert.Equal(0, g.Player.UseAnimation);
        Assert.True(g.Player.ReuseDelay > 0);
        Assert.Equal(UseOutcome.Cooldown, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(g.Player.ReuseDelay);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Player.ResetUse(); // the 'use' command: no delay left either
        Assert.Equal(0, g.Player.ReuseDelay);
    }

    [Fact]
    public void RifleUsesOneBulletPerBurst()
    {
        using var g = Range(10);
        g.Hold(Rifle);
        var shots = Fire(g, 26 * 2, hold: true);
        Assert.Equal(6, shots.Count);
        Assert.Equal(8, g.Player.State.Inventory.Backpack[20].Count);
    }

    [Fact]
    public void LastBulletFiresOnlyTheFirstRound()
    {
        using var g = Range(1);
        g.Hold(Rifle);
        Assert.Single(Fire(g, 30, hold: false)); // follow-ups still need ammo to draw on
        Assert.True(g.Player.State.Inventory.Backpack[20].IsEmpty);
    }

    [Fact]
    public void OtherBurstsUseAmmoPerShot()
    {
        using var g = Range(10);
        var rifle = g.Content.Items.Get(Rifle);
        var burst = g.Content.Items.AddLate(new ItemDef
        {
            Id = "test_burst_gun",
            Name = "Test Burst Gun",
            Category = ItemCategory.Weapon,
            UseStyle = UseStyle.Shoot,
            UseTime = rifle.UseTime,
            UseAnimation = rifle.UseAnimation,
            ReuseDelay = rifle.ReuseDelay,
            Damage = 10,
            DamageType = DamageClass.Ranged,
            UseAmmo = "bullet",
            ShootSpeed = 50,
        });
        g.Player.State.Inventory.Backpack[0] = new ItemStack(burst, 1);
        g.Player.State.Inventory.Select(0);
        Assert.Equal(new[] { 0, 4, 8 }, Fire(g, 20, hold: false));
        Assert.Equal(7, g.Player.State.Inventory.Backpack[20].Count);
    }

    [Theory]
    [InlineData("rapid", 10, 4)] // use speed +15%: 12 → 10 ticks of animation, shots still 4 apart (ceil 3.4)
    [InlineData("hasty", 11, 4)] // +10%
    [InlineData("sluggish", 14, 5)] // −20%: 12 → 14, shots 5 apart
    public void SpeedPrefixesScaleTheBurstButNotTheReuseDelay(string prefix, int animation, int time)
    {
        using var g = Range();
        var rifle = g.Content.Items.Get(Rifle);
        var stack = new ItemStack(rifle, 1, g.Content.Prefixes.Get(prefix));
        g.Player.State.Inventory.Backpack[0] = stack;
        g.Player.State.Inventory.Select(0);
        var shots = Fire(g, 60, hold: true);
        Assert.Equal(animation, g.Player.UseAnimationMax);
        Assert.Equal((int)MathF.Round(rifle.UseAnimation * (1f - stack.Prefix!.UseSpeed)), animation);
        Assert.Equal(new[] { 0, time, 2 * time }, shots.Take(3));
        Assert.Equal(animation + rifle.ReuseDelay, shots[3]); // reuse delay is not scaled
    }

    [Fact]
    public void SwitchingAwayEndsTheBurst()
    {
        using var g = Range();
        g.Hold(Rifle);
        g.Player.State.Inventory.Backpack[1] = new ItemStack(g.Content.Items.Get("wooden_sword"), 1);
        var shots = Fire(g, 20, hold: false, i =>
        {
            if (i == 2) g.Player.State.Inventory.Select(1);
        });
        Assert.Equal(new[] { 0 }, shots);
    }

    [Fact]
    public void EqualTimeGunsFireOncePerAnimation()
    {
        using var g = Range();
        g.Hold("minishark");
        var shots = Fire(g, 30, hold: false);
        Assert.Single(shots);
    }
}
