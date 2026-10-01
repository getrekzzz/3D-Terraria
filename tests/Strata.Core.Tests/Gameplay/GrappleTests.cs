using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Physics;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The Core grappling hook: click-to-use, exact voxel anchors, platforms, tree canopies and letting go.</summary>
public class GrappleTests
{
    private const int ClearTop = 30;

    /// <summary>The test arena with open air up to <see cref="ClearTop"/> cells over the middle and no objects there.</summary>
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var world = g.World.Session.World;
        var batch = world.BeginEdit();
        for (int y = 1; y <= ClearTop; y++)
            for (int z = -6; z <= 6; z++)
                for (int x = -6; x <= 10; x++)
                {
                    batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, 0);
                    batch.SetLiquid(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, 0);
                }
        g.World.Session.Commit(batch);
        var box = new Aabb3i(g.Floor + new Int3(-6, 1, -6), g.Floor + new Int3(11, ClearTop + 1, 7));
        foreach (var o in world.TileObjects.InBox(box).ToList()) world.TileObjects.Remove(o);
        return g;
    }

    /// <summary>A two-cell-thick slab of natural stone (no flags, so it meshes smooth like generated terrain) <paramref name="height"/> cells above the feet.</summary>
    private static void NaturalSlab(TestGame g, int height, int x0 = -4, int x1 = 4)
    {
        var world = g.World.Session.World;
        var batch = world.BeginEdit();
        ushort stone = g.Content.BlockId("stone");
        int y = g.Floor.Y + 1 + height;
        for (int dy = 0; dy < 2; dy++)
            for (int z = -4; z <= 4; z++)
                for (int x = x0; x <= x1; x++)
                    batch.SetBlock(g.Floor.X + x, y + dy, g.Floor.Z + z, stone, 0, CellFlags.None);
        g.World.Session.Commit(batch);
    }

    /// <summary>Clicks with a hook held, aiming along <paramref name="dir"/>.</summary>
    private static void Throw(TestGame g, Vector3 dir, string hook = "grappling_hook")
    {
        g.Hold(hook);
        g.Player.Aim = Vector3.Normalize(dir);
        g.Player.ViewDirection = g.Player.Aim;
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.Grappled, g.World.ItemUse.Use(g.Player, null, true));
    }

    /// <summary>Ticks until the hook reaches <paramref name="state"/>, at most <paramref name="max"/> ticks; returns the ticks taken.</summary>
    private static int TickUntil(TestGame g, HookState state, int max)
    {
        for (int i = 1; i <= max; i++)
        {
            g.World.Tick();
            if (g.Player.Hook.State == state) return i;
        }
        Assert.Fail($"hook still {g.Player.Hook.State} after {max} ticks (wanted {state})");
        return max;
    }

    [Fact]
    public void LatchesOntoNaturalCeilingAndHolds()
    {
        using var g = Arena();
        NaturalSlab(g, 16); // 8 m over the feet
        var feet = g.Player.Position;
        Throw(g, Vector3.UnitY);
        var hook = g.Player.Hook;
        Assert.Equal(HookState.Flying, hook.State);
        TickUntil(g, HookState.Attached, 60);
        var anchor = hook.AnchorCell;
        Assert.Equal(g.Content.BlockId("stone"), g.World.Session.World.GetBlock(anchor));
        Assert.Equal(CellFlags.None, g.World.Session.World.GetFlags(anchor.X, anchor.Y, anchor.Z));
        Assert.Equal(Int3.Down, hook.AnchorNormal);
        Assert.True(hook.PullVelocity(g.Player.Center).Y > 0, "a latched hook does not pull up");

        // Hanging still: nothing nudges the anchor point off the cell, so it keeps holding.
        g.Ticks(120);
        Assert.Equal(feet, g.Player.Position);
        Assert.Equal(HookState.Attached, hook.State);
        Assert.Equal(hook.Target, hook.Head);
    }

    [Fact]
    public void ReleasesWhenTheBlockBreaks()
    {
        using var g = Arena();
        NaturalSlab(g, 16);
        Throw(g, Vector3.UnitY);
        TickUntil(g, HookState.Attached, 60);
        g.World.Editor.SetBlock(g.Player.Hook.AnchorCell, 0);
        g.World.Tick();
        Assert.Equal(HookState.Retracting, g.Player.Hook.State);
        Assert.Equal(Vector3.Zero, g.Player.Hook.PullVelocity(g.Player.Center));
        TickUntil(g, HookState.None, 60);
    }

    [Fact]
    public void PlatformsHold()
    {
        using var g = Arena();
        var batch = g.World.Session.World.BeginEdit();
        byte platform = ShapeCode.Pack(CellShape.Platform, 0);
        for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                batch.SetBlock(g.Floor.X + x, g.Floor.Y + 13, g.Floor.Z + z, g.Content.BlockId("wood"), platform, CellFlags.PlayerPlaced);
        g.World.Session.Commit(batch);
        Throw(g, Vector3.UnitY);
        TickUntil(g, HookState.Attached, 60);
        var anchor = g.Player.Hook.AnchorCell;
        Assert.Equal(CellShape.Platform, ShapeCode.Shape(g.World.Session.World.GetShape(anchor.X, anchor.Y, anchor.Z)));
    }

    [Fact]
    public void TreeCanopyHolds()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        // An oak 3 m along +X; its trunk is the middle column of the 3×3 box.
        var tree = new TileObjectInstance(g.Content.TileObjects.Get("oak_tree"), g.Floor + new Int3(5, 1, -1), 0, 0);
        Assert.True(world.TileObjects.TryAdd(tree));
        var (trunkMin, trunkMax) = tree.TrunkBox;
        // Aim at the canopy above the near (off-trunk) column.
        var canopy = new Vector3(tree.Anchor.X + 0.5f, tree.Anchor.Y + 17f, g.Floor.Z + 0.5f) * Units.CellSize;
        Throw(g, canopy - g.Player.Eye);
        TickUntil(g, HookState.Attached, 60);
        var anchor = g.Player.Hook.AnchorCell;
        Assert.Same(tree, world.TileObjects.At(anchor));
        Assert.True(anchor.X + 1 <= trunkMin.X, $"latched to the trunk column at {anchor}");
        Assert.True(anchor.Y >= trunkMax.Y, $"latched below the canopy at {anchor}");
    }

    [Fact]
    public void TheTreeAPlayerStandsInDoesNotCatchTheThrow()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        // The player's eye is inside an oak's box (its canopy column reaches down beside the trunk).
        var tree = new TileObjectInstance(g.Content.TileObjects.Get("oak_tree"), g.Floor + new Int3(-2, 1, -1), 0, 0);
        Assert.True(world.TileObjects.TryAdd(tree));
        Assert.Same(tree, world.TileObjects.At(VoxelMotion.CellOf(g.Player.Eye)));
        NaturalSlab(g, 16, x0: 1, x1: 8);
        Throw(g, new Vector3(0.3f, 1f, 0f));
        TickUntil(g, HookState.Attached, 60);
        Assert.Equal(g.Content.BlockId("stone"), world.GetBlock(g.Player.Hook.AnchorCell));
    }

    [Fact]
    public void MissRetracts()
    {
        using var g = Arena();
        Throw(g, Vector3.UnitY);
        var hook = g.Player.Hook;
        Assert.Equal(HookState.Flying, hook.State);
        Assert.False(hook.Hit);
        int limit = (int)MathF.Ceiling(hook.Params!.Reach * Units.CellSize / hook.Params.Speed * 2f * Units.TicksPerSecond);
        int outward = TickUntil(g, HookState.Retracting, limit);
        Assert.True(Vector3.Distance(hook.Head, g.Player.Hand) > hook.Params.Reach * Units.CellSize * 0.95f, "turned back before the end of its reach");
        TickUntil(g, HookState.None, limit - outward);
    }

    [Fact]
    public void ClickToUse()
    {
        using var g = Arena();
        g.Hold("grappling_hook");
        Assert.Equal(UseOutcome.Grappled, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(HookState.Flying, g.Player.Hook.State);
        Assert.Equal("grappling_hook", g.Player.Hook.Item!.Id);
        // Hooks do not auto-reuse: holding the button does nothing more; a fresh click lets go.
        g.Player.ResetUse();
        Assert.Equal(UseOutcome.None, g.World.ItemUse.Use(g.Player, null, false));
        Assert.Equal(UseOutcome.Grappled, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(HookState.Retracting, g.Player.Hook.State);
    }

    [Fact]
    public void BestHookPrefersReach()
    {
        using var g = Arena();
        var inv = g.Player.State.Inventory;
        inv.Backpack[2] = new ItemStack(g.Content.Items.Get("grappling_hook"), 1);
        inv.Backpack[20] = new ItemStack(g.Content.Items.Get("dual_hook"), 1);
        var best = GrappleHook.Best(g.Player.State, g.World.Uses, out var item);
        Assert.Equal("dual_hook", item!.Id);
        Assert.Same(g.World.Uses[item].Grapple, best);
        inv.Backpack[20] = ItemStack.Empty;
        Assert.NotNull(GrappleHook.Best(g.Player.State, g.World.Uses, out item));
        Assert.Equal("grappling_hook", item!.Id);
        inv.Backpack[2] = ItemStack.Empty;
        Assert.Null(GrappleHook.Best(g.Player.State, g.World.Uses, out item));
        Assert.Null(item);
    }

    [Fact]
    public void CobwebsWaterAndDecorDoNotHold()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        var at = g.Floor + new Int3(3, 2, 3);
        var batch = world.BeginEdit();
        batch.SetBlock(at, g.Content.BlockId("cobweb"));
        batch.SetLiquid(at + Int3.Up, LiquidCode.Full(LiquidType.Water));
        batch.SetBlock(at + new Int3(1, 0, 0), g.Content.BlockId("stone"));
        g.World.Session.Commit(batch);
        var tuft = new TileObjectInstance(g.Content.TileObjects.Get("grass_tuft"), g.Floor + new Int3(-3, 1, 3), 0, 0);
        Assert.True(world.TileObjects.TryAdd(tuft));

        Assert.False(GrappleHook.Latchable(world, at));
        Assert.False(GrappleHook.Latchable(world, at + Int3.Up));
        Assert.False(GrappleHook.Latchable(world, tuft.Anchor));
        Assert.False(GrappleHook.Latchable(world, at + new Int3(0, 3, 0)));
        Assert.True(GrappleHook.Latchable(world, at + new Int3(1, 0, 0)));
    }

    [Fact]
    public void AnEngineBodyAdvancesItsOwnHook()
    {
        using var g = Arena();
        NaturalSlab(g, 16);
        g.Player.HasBody = true;
        Throw(g, Vector3.UnitY);
        var hook = g.Player.Hook;
        var start = hook.Head;
        g.Ticks(30);
        Assert.Equal(HookState.Flying, hook.State); // the world tick leaves it to the body
        Assert.Equal(start, hook.Head);
        for (int i = 0; i < 60 && hook.State == HookState.Flying; i++) hook.Tick(g.World.Session.World, Units.SecondsPerTick, g.Player.Hand);
        Assert.Equal(HookState.Attached, hook.State);
    }

    [Fact]
    public void DeathPutsTheHookAway()
    {
        using var g = Arena();
        NaturalSlab(g, 16);
        Throw(g, Vector3.UnitY);
        TickUntil(g, HookState.Attached, 60);
        g.Player.State.Vitals.Life = 0;
        g.World.Tick();
        Assert.Equal(HookState.None, g.Player.Hook.State);
    }
}
