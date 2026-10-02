using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class WorldInteractionTests
{
    [Fact]
    public void MiningBrushIsTheAlignedGridBlock()
    {
        // The same 1 m block whichever of its cells (or face) is hit, also at negative coordinates.
        var a = MiningRules.Brush(new Int3(10, 10, 10), new Int3(0, 1, 0), new Vector3(0.7f, 0.99f, 0.2f), 2).ToHashSet();
        var b = MiningRules.Brush(new Int3(11, 11, 11), new Int3(-1, 0, 0), Vector3.Zero, 2).ToHashSet();
        Assert.Equal(8, a.Count);
        Assert.True(a.SetEquals(b));
        Assert.All(a, c => Assert.True(c.X is 10 or 11 && c.Y is 10 or 11 && c.Z is 10 or 11));
        Assert.Equal(new Int3(-2, -4, 0), BlockGrid.MinOf(new Int3(-1, -3, 1)));
        Assert.Single(MiningRules.Brush(new Int3(1, 1, 1), new Int3(1, 0, 0), Vector3.Zero, 1));
    }

    [Fact]
    public void BlockShapesScaleUpAndRotate()
    {
        var min = new Int3(4, 6, 8);
        Assert.Equal(8, BlockGrid.Pattern(min, CellShape.Full, 0).Count);
        Assert.All(BlockGrid.Pattern(min, CellShape.HalfBottom, 0), p => Assert.Equal(6, p.Cell.Y));
        // A 1 m ramp rising towards +Z: full cell low at the back, ramps over and in front of it, open top front.
        var ramp = BlockGrid.Pattern(min, CellShape.Slope, 0).ToDictionary(p => p.Cell, p => p.Shape);
        Assert.Equal(6, ramp.Count);
        Assert.Equal(CellShape.Full, ShapeCode.Shape(ramp[new Int3(4, 6, 9)]));
        Assert.Equal(CellShape.Slope, ShapeCode.Shape(ramp[new Int3(4, 6, 8)]));
        Assert.Equal(CellShape.Slope, ShapeCode.Shape(ramp[new Int3(4, 7, 9)]));
        Assert.False(ramp.ContainsKey(new Int3(4, 7, 8)));
        // One quarter turn (rising towards -X, like single cells): the full cell moves to the low-x side.
        var turned = BlockGrid.Pattern(min, CellShape.Slope, 1).ToDictionary(p => p.Cell, p => p.Shape);
        Assert.Equal(CellShape.Full, ShapeCode.Shape(turned[new Int3(4, 6, 8)]));
        Assert.False(turned.ContainsKey(new Int3(5, 7, 8)));
        Assert.All(turned.Values, code => Assert.Equal(1, ShapeCode.Rotation(code)));
    }

    [Fact]
    public void DamageAccumulatesAndForgets()
    {
        var t = new BlockDamageTracker { ForgetAfterTicks = 100 };
        var c = new Int3(1, 2, 3);
        Assert.Equal(35, t.Hit(c, 35, 0));
        Assert.Equal(70, t.Hit(c, 35, 50));
        Assert.Equal(35, t.Hit(c, 35, 500));
        var stone = TestPaths.Content.Blocks.Get("stone");
        Assert.Equal(150f, MiningRules.Health(stone));
        Assert.False(MiningRules.CanMine(TestPaths.Content.Blocks.Get("obsidian"), 35));
        Assert.True(MiningRules.CanMine(TestPaths.Content.Blocks.Get("obsidian"), 55));
    }

    [Fact]
    public void BlocksNeedSupportAndFreeSpace()
    {
        var world = TestWorlds.Filled("air");
        var db = world.Content;
        var stone = db.Blocks.Get("stone");
        var none = Array.Empty<(Vector3, Vector3)>();
        Assert.Equal(PlacementResult.NoSupport, PlacementRules.CanPlaceBlock(world, new Int3(40, 40, 40), stone, none));
        var b = world.BeginEdit();
        b.SetBlock(40, 39, 40, db.BlockId("stone"));
        b.Commit();
        Assert.Equal(PlacementResult.Ok, PlacementRules.CanPlaceBlock(world, new Int3(40, 40, 40), stone, none));
        Assert.Equal(PlacementResult.Occupied, PlacementRules.CanPlaceBlock(world, new Int3(40, 39, 40), stone, none));
        var player = (new Vector3(39.5f, 40f, 39.5f), new Vector3(41f, 43.6f, 41f));
        Assert.Equal(PlacementResult.BlockedByEntity, PlacementRules.CanPlaceBlock(world, new Int3(40, 40, 40), stone, new[] { player }));
    }

    [Fact]
    public void TileObjectsNeedTheirAnchor()
    {
        var world = TestWorlds.Filled("air");
        var db = world.Content;
        var table = db.TileObjects.Get("wooden_table");
        Assert.Equal(PlacementResult.NoSupport, PlacementRules.CanPlaceObject(world, table, new Int3(40, 41, 40), 0));
        var b = world.BeginEdit();
        for (int x = 38; x < 46; x++)
            for (int z = 38; z < 46; z++)
                b.SetBlock(x, 40, z, db.BlockId("wood"));
        b.Commit();
        Assert.Equal(PlacementResult.Ok, PlacementRules.CanPlaceObject(world, table, new Int3(40, 41, 40), 0));
        world.TileObjects.TryAdd(new TileObjectInstance(table, new Int3(40, 41, 40), 0, 0));
        Assert.Equal(PlacementResult.Occupied, PlacementRules.CanPlaceObject(world, db.TileObjects.Get("wooden_chair"), new Int3(41, 41, 41), 0));
    }

    [Fact]
    public void DroppedItemsFallLandAndGetCollected()
    {
        var world = TestWorlds.Filled("air");
        var db = world.Content;
        var b = world.BeginEdit();
        for (int x = 30; x < 50; x++)
            for (int z = 30; z < 50; z++)
                b.SetBlock(x, 40, z, db.BlockId("stone"));
        b.Commit();
        var sys = new DroppedItemSystem();
        sys.Spawn(new ItemStack(db.Items.Get("gel"), 3), new Vector3(20f, 25f, 20f), Vector3.Zero, pickupDelay: 0);
        sys.Spawn(new ItemStack(db.Items.Get("gel"), 2), new Vector3(20.3f, 25f, 20f), Vector3.Zero, pickupDelay: 0);
        var far = new Vector3(100, 100, 100);
        for (int i = 0; i < 180; i++) sys.Tick(world, 1f / 60f, far, s => s);
        Assert.Single(sys.Items);                  // merged
        Assert.Equal(5, sys.Items[0].Stack.Count);
        Assert.InRange(sys.Items[0].Position.Y, 20.5f, 20.7f); // resting on the stone top at y = 41 cells = 20.5 m
        Assert.True(sys.Items[0].Grounded);

        var collected = new Inventory(4);
        var player = sys.Items[0].Position + new Vector3(1.5f, 0.5f, 0); // inside magnet range, outside pickup range
        for (int i = 0; i < 60 && sys.Items.Count > 0; i++) sys.Tick(world, 1f / 60f, player, s => collected.Add(s));
        Assert.Empty(sys.Items);
        Assert.Equal(5, collected.Count(db.Items.Get("gel")));
    }

    [Fact]
    public void BreakingAWaterBoltBookDropsTheWaterBolt()
    {
        using var g = TestGame.Create();
        var store = g.World.Session.World.TileObjects;
        var plain = new TileObjectInstance(g.Content.TileObjects.Get("book"), g.Floor + new Int3(2, 1, 0), 0, 1);
        var special = new TileObjectInstance(g.Content.TileObjects.Get("book"), g.Floor + new Int3(-2, 1, 0), 0, 2) { PendingLoot = "water_bolt_book" };
        Assert.True(store.TryAdd(plain));
        Assert.True(store.TryAdd(special));
        g.World.Editor.BreakObject(special, dropItems: true);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "water_bolt");
        Assert.DoesNotContain(g.World.Drops.Items, d => d.Stack.Item!.Id == "book");
        g.World.Editor.BreakObject(plain, dropItems: true);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "book");
    }
}
