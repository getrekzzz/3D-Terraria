using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Weapons smash pots and mow grass (P3-23): sword swings and friendly projectiles break <c>breakByAttack</c> objects.</summary>
public class AttackBreaksObjectsTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.ViewDirection = Vector3.UnitZ;
        g.Player.Aim = Vector3.Normalize(new Vector3(0, -0.3f, 1));
        g.Player.AimDistance = 6f;
        return g;
    }

    /// <summary>Puts an object on the floor <paramref name="cellsAhead"/> cells in front of the player (+Z).</summary>
    private static TileObjectInstance Place(TestGame g, string id, int cellsAhead)
    {
        var feet = g.Player.FeetCell;
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), new Int3(feet.X, g.Floor.Y + 1, feet.Z + cellsAhead), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    private static Vector3 Center(TileObjectInstance obj) => (new Vector3(obj.Anchor.X, obj.Anchor.Y, obj.Anchor.Z) + Vector3.One * 0.5f) * Units.CellSize;

    private static bool Present(TestGame g, TileObjectInstance obj) => g.World.Session.World.TileObjects.Get(obj.Id) == obj;

    [Fact]
    public void ASwordSwingShattersAPotAndSpillsItsLoot()
    {
        using var g = Arena();
        var pot = Place(g, "pot", 2);
        int drops = g.World.Drops.Items.Count;
        g.Hold("copper_broadsword");
        g.Player.ResetUse();
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(g.Player.UseAnimationMax + 2);
        Assert.False(Present(g, pot), "the pot broke");
        Assert.True(g.World.Drops.Items.Count > drops, "and spilled its loot");
    }

    [Fact]
    public void AnArrowMowsGrassAndFliesOn()
    {
        using var g = Arena();
        var tuft = Place(g, "grass_tuft", 6);
        // Skimmed level through the tuft's cell, so it is the grass and not the ground the arrow meets.
        var arrow = g.World.Projectiles.Spawn(g.Content.Projectiles.Get("wooden_arrow"), Center(tuft) - Vector3.UnitZ * 1.5f + Vector3.UnitY * 0.1f,
            Vector3.UnitZ * 20f, g.Player, 5, 0, Strata.Core.Content.Defs.DamageClass.Ranged, 0);
        for (int i = 0; i < 20 && Present(g, tuft); i++) g.Ticks(1);
        Assert.False(Present(g, tuft), "the arrow cut the grass");
        Assert.Contains(arrow, g.World.Projectiles.All); // and keeps flying

        // A shot that lands in a tuft still cuts it.
        var second = Place(g, "grass_tuft", 4);
        g.Hold("wooden_bow");
        g.Player.State.Inventory.Backpack[30] = new ItemStack(g.Content.Items.Get("wooden_arrow"), 5);
        g.Player.Aim = Vector3.Normalize(Center(second) - g.Player.Eye);
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        for (int i = 0; i < 60 && Present(g, second); i++) g.Ticks(1);
        Assert.False(Present(g, second), "the landing arrow cut the grass");
    }

    [Fact]
    public void SolidObjectsAndHostileShotsDoNotBreak()
    {
        using var g = Arena();
        var chair = Place(g, "wooden_chair", 2);
        var pot = Place(g, "pot", 10); // out of the sword's reach
        g.Hold("copper_broadsword");
        g.Player.ResetUse();
        g.World.ItemUse.Use(g.Player, null, true);
        g.Ticks(g.Player.UseAnimationMax + 2);
        Assert.True(Present(g, chair), "a chair is not breakByAttack");
        // A hostile shot through the pot leaves it.
        var hostile = g.Content.Projectiles.All.First(p => !p.Friendly && p.Contact);
        Assert.True(Present(g, pot));
        var at = Center(pot);
        g.World.Projectiles.Spawn(hostile, at - Vector3.UnitZ, Vector3.UnitZ * 4f, null, 10, 0, Strata.Core.Content.Defs.DamageClass.None, 0);
        g.Ticks(30);
        Assert.True(Present(g, pot), "a hostile shot leaves pots alone");
    }
}
