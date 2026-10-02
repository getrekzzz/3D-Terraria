using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class PetTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    private static void Equip(TestGame g, string id)
    {
        g.Hold(id, slot: 5);
        Assert.True(g.Player.State.Inventory.Equip(5), $"{id} did not equip");
    }

    private static Vector3 Cell(TestGame g, int dx, int dz) => TestGame.CellFeet(g.Floor + new Int3(dx, 1, dz));

    [Fact]
    public void LightPetFollowsAndIsDismissedWhenUnequipped()
    {
        using var g = Arena();
        var inv = g.Player.State.Inventory;
        Equip(g, "shadow_orb");
        Assert.Equal("shadow_orb", inv.EquippedLightPet?.Id);
        g.Ticks(1);
        var orb = Assert.Single(g.World.Projectiles.All);
        Assert.Equal(MiscSlot.LightPet, orb.PetSlot);
        Assert.NotNull(orb.Def.Light);
        Assert.Same(orb, g.World.Projectiles.PetIn(g.Player, MiscSlot.LightPet));
        Assert.Equal(1, g.World.Projectiles.PetCount(lights: true));

        // Teleport the owner ~11 m across the arena: the orb catches up and settles near the shoulder.
        g.Player.Position = Cell(g, -8, -8);
        g.Ticks(120);
        g.Player.Position = Cell(g, 8, 8);
        g.Ticks(120);
        Assert.True(Vector3.Distance(orb.Position, g.Player.Center) < 3f, $"the orb is {Vector3.Distance(orb.Position, g.Player.Center):0.0} m away");
        g.Ticks(600);
        Assert.Same(orb, Assert.Single(g.World.Projectiles.All)); // pets never expire

        inv.Misc[(int)MiscSlot.LightPet] = ItemStack.Empty;
        g.Ticks(1);
        Assert.Empty(g.World.Projectiles.All);
    }

    [Fact]
    public void WalkingPetRunsAfterItsOwnerAndSwapsWithTheItem()
    {
        using var g = Arena();
        Equip(g, "fish");
        Equip(g, "fairy_bell");
        g.Ticks(1);
        Assert.Equal(2, g.World.Projectiles.All.Count);
        var penguin = g.World.Projectiles.PetIn(g.Player, MiscSlot.Pet)!;
        Assert.Equal("baby_penguin", penguin.Def.Id);
        Assert.False(penguin.Def.Pet!.Flies);

        g.Player.Position = Cell(g, -8, -8);
        g.Ticks(120);
        g.Player.Position = Cell(g, 8, 8);
        g.Ticks(180);
        Assert.True(Vector3.Distance(penguin.Position, g.Player.Position) < 3f, "the penguin was left behind");
        Assert.True(penguin.Grounded, "the penguin should be on the floor");
        Assert.True(penguin.Position.Y < g.Player.Position.Y + 0.5f);

        // A different pet item in the slot swaps the pet; a far owner pulls it along at once.
        Equip(g, "zephyr_fish");
        g.Ticks(1);
        var fish = g.World.Projectiles.PetIn(g.Player, MiscSlot.Pet)!;
        Assert.Equal("zephyr_fish_pet", fish.Def.Id);
        Assert.Equal(2, g.World.Projectiles.All.Count);
        g.Player.Position += new Vector3(0, 0, ProjectileSystem.PetTeleport + 10f);
        g.Ticks(1);
        Assert.True(Vector3.Distance(fish.Position, g.Player.Center) < 4f);
    }

    [Fact]
    public void PetsVanishWhileTheOwnerIsDeadAndReturnOnRespawn()
    {
        using var g = Arena();
        Equip(g, "crimson_heart");
        g.Ticks(1);
        Assert.Single(g.World.Projectiles.All);
        g.Player.State.Vitals.Life = 0;
        g.Ticks(2);
        Assert.Empty(g.World.Projectiles.All);
        g.Ticks(GameWorld.RespawnDelayTicks + 5);
        Assert.True(g.Player.Alive);
        Assert.Single(g.World.Projectiles.All);
    }

    [Fact]
    public void MiscPetSlotsSurviveASaveRoundTrip()
    {
        using var g = Arena();
        Equip(g, "strange_glowing_mushroom");
        Equip(g, "magic_lantern");
        var dir = TestWorlds.TempDir();
        GameWorld.WritePlayer(dir, g.Player.State);
        var loaded = GameWorld.LoadOrCreatePlayer(dir, g.Player.State.Name, g.Content).Inventory;
        Assert.Equal("strange_glowing_mushroom", loaded.EquippedPet?.Id);
        Assert.Equal("magic_lantern", loaded.EquippedLightPet?.Id);
        Assert.Contains("magic_lantern", PlayerSave.From(g.Player.State).Serialize());
    }
}
