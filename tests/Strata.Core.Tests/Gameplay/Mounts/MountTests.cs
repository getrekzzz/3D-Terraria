using System.Numerics;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Mounts;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay.Mounts;

public class MountTests
{
    private const float Gravity = 32f; // data/config/movement.json
    private const float Dt = 1f / 60f;

    private static MountParams Params(TestGame g, string item) => g.World.Uses[g.Content.Items.Get(item)].Ride!;

    /// <summary>Simulates a held jump from the ground on flat land; returns the apex height (m) and the time airborne.</summary>
    private static (float Apex, float Airborne) Jump(MountRide ride, float holdSeconds)
    {
        float y = 0, apex = 0, t = 0;
        var v = MountMotion.Step(ride, Vector3.Zero, true, new MountInput(Vector3.Zero, true, true, false), Gravity, Dt);
        for (int i = 0; i < 60 * 10; i++)
        {
            t += Dt;
            y += v.Y * Dt;
            apex = MathF.Max(apex, y);
            if (y <= 0 && i > 0) return (apex, t);
            v = MountMotion.Step(ride, v, false, new MountInput(Vector3.Zero, false, t < holdSeconds, false), Gravity, Dt);
        }
        return (apex, t);
    }

    [Fact]
    public void MountItemsSummonAndDismountWithTheirBuff()
    {
        using var g = TestGame.Create();
        g.Hold("slimy_saddle");
        Assert.Equal(UseOutcome.Mounted, g.World.ItemUse.Use(g.Player, null, true));
        Assert.NotNull(g.Player.Mount);
        Assert.Equal("slime", g.Player.Mount!.Config.Id);
        Assert.True(g.Player.State.Buffs.Has("slime_mount"));
        g.Player.UseCooldown = 0;
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.Null(g.Player.Mount);
        Assert.False(g.Player.State.Buffs.Has("slime_mount"));
    }

    [Fact]
    public void QuickMountFindsAMountAndSwapsMounts()
    {
        using var g = TestGame.Create();
        Assert.False(MountRules.QuickMount(g.World, g.Player));
        g.Player.State.Inventory.Backpack.Add(new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("fuzzy_carrot"), 1));
        Assert.True(MountRules.QuickMount(g.World, g.Player));
        Assert.Equal("bunny", g.Player.Mount!.Config.Id);
        // Another mount's item swaps straight to it.
        MountRules.Toggle(g.World, g.Player, g.Content.Items.Get("honeyed_goggles"), Params(g, "honeyed_goggles"));
        Assert.Equal("bee", g.Player.Mount!.Config.Id);
        Assert.False(g.Player.State.Buffs.Has("bunny_mount"));
        Assert.True(MountRules.QuickMount(g.World, g.Player)); // dismounts
        Assert.Null(g.Player.Mount);
    }

    [Fact]
    public void QuickMountPrefersTheMountSlot()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        inv.Backpack.Add(new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("fuzzy_carrot"), 1));
        inv.Misc[(int)Strata.Core.Gameplay.Items.MiscSlot.Mount] = new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("cosmic_car_key"), 1);
        Assert.True(MountRules.QuickMount(g.World, g.Player));
        Assert.Equal("ufo", g.Player.Mount!.Config.Id);
    }

    [Fact]
    public void TheSlimeJumpsFarHigherThanOnFoot()
    {
        using var g = TestGame.Create();
        // On foot: 9.2 m/s take-off under full gravity reaches about 1.3 m (plus the jump hold).
        float onFoot = 9.2f * 9.2f / (2f * Gravity);
        var (apex, _) = Jump(new MountRide(Params(g, "slimy_saddle"), "slimy_saddle"), 0.4f);
        Assert.True(apex > onFoot * 2.5f, $"slime apex {apex:0.00} m vs {onFoot:0.00} m on foot");
        var (bunny, _) = Jump(new MountRide(Params(g, "fuzzy_carrot"), "fuzzy_carrot"), 0.4f);
        Assert.True(bunny > onFoot * 1.5f && bunny < apex, $"bunny apex {bunny:0.00}");
    }

    [Fact]
    public void TheBeeFliesForItsFlightTimeAndRefillsOnLanding()
    {
        using var g = TestGame.Create();
        var ride = new MountRide(Params(g, "honeyed_goggles"), "honeyed_goggles");
        var v = Vector3.Zero;
        var hold = new MountInput(Vector3.Zero, false, true, false);
        for (int i = 0; i < 30; i++) v = MountMotion.Step(ride, v, false, hold, Gravity, Dt); // past the jump hold
        Assert.True(ride.Flying);
        Assert.True(v.Y > 0);
        for (int i = 0; i < 60 * 4; i++) v = MountMotion.Step(ride, v, false, hold, Gravity, Dt);
        Assert.Equal(0f, ride.FlightLeft);
        Assert.True(v.Y < 0, "out of flight the bee falls");
        MountMotion.Step(ride, v, true, default, Gravity, Dt);
        Assert.Equal(ride.Config.FlightTime, ride.FlightLeft);
    }

    [Fact]
    public void TheUfoFliesFreelyAndHovers()
    {
        using var g = TestGame.Create();
        var ride = new MountRide(Params(g, "cosmic_car_key"), "cosmic_car_key");
        var v = Vector3.Zero;
        for (int i = 0; i < 60 * 20; i++) v = MountMotion.Step(ride, v, false, new MountInput(Vector3.Zero, false, true, false), Gravity, Dt);
        Assert.Equal(ride.Config.FlySpeed, v.Y, 2); // still climbing after twenty seconds
        for (int i = 0; i < 120; i++) v = MountMotion.Step(ride, v, false, default, Gravity, Dt);
        Assert.Equal(0f, v.Y, 2); // hovers when let go
        var fast = MountMotion.Step(ride, Vector3.Zero, true, new MountInput(Vector3.UnitX, false, false, false), Gravity, 1f);
        Assert.Equal(ride.Config.RunSpeed, fast.X, 2);
    }

    [Fact]
    public void TheSlimeStompsWhatItLandsOn()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold("slimy_saddle");
        g.World.ItemUse.Use(g.Player, null, true);
        var zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(0.3f, 0, 0))!;
        int life = zombie.Life;
        g.Player.Position = zombie.Center + new Vector3(0, zombie.HalfExtents.Y + 0.1f, 0);
        var bounce = MountMotion.TryStomp(g.World, g.Player, g.Player.Mount!, -8f);
        Assert.NotNull(bounce);
        Assert.True(zombie.Life < life);
        Assert.Null(MountMotion.TryStomp(g.World, g.Player, g.Player.Mount!, 1f)); // rising: no stomp
    }

    [Fact]
    public void DeathDismounts()
    {
        using var g = TestGame.Create();
        g.Hold("cosmic_car_key");
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.NotNull(g.Player.Mount);
        g.Player.State.Vitals.DamageDirect(10000);
        g.Ticks(2);
        Assert.Null(g.Player.Mount);
    }

    [Fact]
    public void BossesDropTheirMounts()
    {
        Assert.True(TestPaths.Content.LootTables.Contains("extra_king_slime"));
        Assert.True(TestPaths.Content.LootTables.Contains("extra_queen_bee"));
        Assert.True(TestPaths.Content.LootTables.Contains("extra_martian_saucer"));
    }
}
