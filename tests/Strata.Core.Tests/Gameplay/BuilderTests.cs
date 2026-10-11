using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Mounts;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class BuilderTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static TileObjectInstance Place(TestGame g, string id, Int3 at, int rotation = 0)
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), at, rotation, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    /// <summary>Wire along the floor blocks from x0 to x1 (inclusive) at the given z.</summary>
    private static void WireFloor(TestGame g, int x0, int x1, int z)
    {
        for (int x = x0; x <= x1; x++) g.World.Wires.Place(g.Floor + new Int3(x, 0, z));
    }

    [Fact]
    public void ALeverOpensAWiredDoor()
    {
        using var g = Arena();
        var door = Place(g, "wooden_door", g.Floor + new Int3(6, 1, 3));
        var lever = Place(g, "lever", g.Floor + new Int3(2, 1, 3));
        WireFloor(g, 2, 6, 3);
        g.World.Wires.Place(g.Floor + new Int3(6, 1, 3)); // up into the door
        Assert.Equal(InteractOutcome.Switched, g.World.Interaction.Interact(g.Player, lever));
        Assert.True(door.Active);
        g.World.Interaction.Interact(g.Player, lever);
        Assert.False(door.Active);
    }

    [Fact]
    public void AnUnwiredLeverDoesNothing()
    {
        using var g = Arena();
        var door = Place(g, "wooden_door", g.Floor + new Int3(6, 1, 3));
        var lever = Place(g, "lever", g.Floor + new Int3(2, 1, 3));
        WireFloor(g, 2, 6, 3); // runs under the door but never into it
        g.World.Interaction.Interact(g.Player, lever);
        Assert.False(door.Active);
    }

    [Fact]
    public void SteppingOnAPressurePlateFiresADartTrap()
    {
        using var g = Arena();
        var plate = g.Floor + new Int3(0, 1, 0);
        Place(g, "pressure_plate", plate);
        Place(g, "dart_trap", g.Floor + new Int3(5, 1, 0), rotation: 1);
        WireFloor(g, 0, 5, 0);
        g.World.Wires.Place(g.Floor + new Int3(5, 1, 0));
        g.Player.Position = TestGame.CellFeet(g.Floor + new Int3(3, 1, 3));
        g.Ticks(1);
        Assert.DoesNotContain(g.World.Projectiles.All, p => p.Def.Id == "trap_dart");
        g.Player.Position = TestGame.CellFeet(plate);
        g.Ticks(1);
        Assert.Single(g.World.Projectiles.All, p => p.Def.Id == "trap_dart");
        g.Ticks(1); // standing still does not fire again
        Assert.Single(g.World.Projectiles.All, p => p.Def.Id == "trap_dart");
    }

    [Fact]
    public void TheRedWrenchLaysWireFromTheInventory()
    {
        using var g = Arena();
        g.Hold("red_wrench");
        g.Player.State.Inventory.Backpack[5] = new Core.Gameplay.Items.ItemStack(g.Content.Items.Get("wire"), 3);
        var cell = g.Floor + new Int3(0, 0, 3);
        g.AimAt(TestGame.CellCenter(cell));
        Assert.Equal(UseOutcome.Wired, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.True(g.World.Wires.Has(cell));
        Assert.Equal(2, g.Player.State.Inventory.Backpack[5].Count);

        g.Player.UseCooldown = 0;
        g.Hold("wire_cutter", slot: 1);
        Assert.Equal(UseOutcome.Wired, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.False(g.World.Wires.Has(cell));
        Assert.Equal(3, g.Player.State.Inventory.Backpack[5].Count);
    }

    [Fact]
    public void TheBugNetCatchesCritters()
    {
        using var g = Arena();
        var bunny = g.World.Npcs.Spawn(g.Content.Enemies.Get("bunny"), g.Player.Position + new Vector3(0, 0, -1.2f))!;
        g.Hold("bug_net");
        g.AimAt(bunny.Center);
        Assert.Equal(UseOutcome.Caught, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.DoesNotContain(bunny, g.World.Npcs.All);
        Assert.Contains(g.Player.State.Inventory.Backpack.Stacks, s => s.Item?.Id == "bunny");
    }

    [Fact]
    public void AMinecartRidesToTheEndOfItsTrack()
    {
        using var g = Arena();
        for (int z = 0; z < 6; z++) Place(g, "minecart_track", g.Floor + new Int3(0, 1, -z));
        Place(g, "minecart_track", g.Floor + new Int3(1, 1, -5), rotation: 1); // a corner at the far end
        g.Player.Position = TestGame.CellFeet(g.Floor + Int3.Up);
        g.Player.Aim = -Vector3.UnitZ;
        g.Hold("minecart");
        Assert.Equal(UseOutcome.Mounted, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        var ride = g.Player.Minecart!;
        for (int i = 0; i < 300; i++) Minecarts.Step(g.World.Session.World, ride, 1f, 1f / 60f);
        Assert.Equal(g.Floor + new Int3(1, 1, -5), ride.Cell);
        Assert.Null(ride.Next);
        Assert.Equal(0f, ride.Speed);
    }

    private static float SpeedAfterAThirdOfASecond(bool upgraded)
    {
        using var g = Arena();
        for (int z = 0; z < 6; z++) Place(g, "minecart_track", g.Floor + new Int3(0, 1, -z));
        g.Player.State.MinecartUpgrade = upgraded;
        g.Player.Position = TestGame.CellFeet(g.Floor + Int3.Up);
        g.Player.Aim = -Vector3.UnitZ;
        g.Hold("minecart");
        Assert.Equal(UseOutcome.Mounted, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        var ride = g.Player.Minecart!;
        Assert.Equal(upgraded ? ItemUseSystem.MechanicalCartSpeed : 1f, ride.SpeedScale);
        for (int i = 0; i < 20; i++) Minecarts.Step(g.World.Session.World, ride, 1f, 1f / 60f);
        return ride.Speed; // still accelerating: the top speed scales the same way (Minecarts.Step)
    }

    [Fact]
    public void MinecartUpgradeKitSpeedsTheCart()
    {
        float plain = SpeedAfterAThirdOfASecond(false), mechanical = SpeedAfterAThirdOfASecond(true);
        Assert.True(plain > 1f);
        Assert.Equal(plain * ItemUseSystem.MechanicalCartSpeed, mechanical, 1);
    }
}
