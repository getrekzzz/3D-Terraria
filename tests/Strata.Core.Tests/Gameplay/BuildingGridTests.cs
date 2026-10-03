using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Building with 1 m blocks on half-block ground, and holding the use button while building.</summary>
public class BuildingGridTests
{
    [Fact]
    public void RaisedBlocksSitOneCellAboveTheGrid()
    {
        Assert.Equal(new Int3(4, 7, 8), BlockGrid.MinOf(new Int3(5, 8, 9), raised: true));
        Assert.Equal(new Int3(4, 7, 8), BlockGrid.MinOf(new Int3(4, 7, 8), raised: true));
        Assert.Equal(-1, BlockGrid.MinOf(new Int3(0, 0, 0), raised: true).Y);
        Assert.Equal(-3, BlockGrid.MinOf(new Int3(0, -2, 0), raised: true).Y);
        Assert.Equal(8, BlockGrid.Cells(new Int3(0, 11, 0), raised: true).Count());
        Assert.All(BlockGrid.Pattern(new Int3(0, 12, 0), CellShape.HalfBottom, 0, raised: true), p => Assert.Equal(11, p.Cell.Y));
    }

    [Fact]
    public void FullBlocksSitWholeOnGroundOffTheGrid()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        ushort wood = g.Content.BlockId("wood"), stone = g.Content.BlockId("stone");
        var ground = GroundInFront(g, offGrid: true);
        g.Hold("wood", 30);
        g.AimAt(TestGame.CellCenter(ground) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), true));

        // The whole 1 m block stands on the ground half a block above the grid, instead of losing its bottom half.
        var block = BlockGrid.Cells(ground + Int3.Up, raised: true).ToList();
        Assert.Equal(ground.Y + 1, block.Min(c => c.Y));
        Assert.All(block, c => Assert.Equal(wood, world.GetBlock(c)));
        Assert.All(block, c => Assert.True(BlockGrid.IsRaised(world, c)));
        Assert.Equal(22, g.Player.State.Inventory.Held.Count);
        Assert.Equal(stone, world.GetBlock(ground));

        // Digging the ground next to it never takes a bite out of it.
        Assert.DoesNotContain(MiningRules.Brush(ground, Int3.Up, Vector3.Zero, 2, world), c => BlockGrid.IsRaised(world, c));

        // Building on it: on top and against its upper half the next blocks keep its height.
        var top = block.Where(c => c.Y == ground.Y + 2).MaxBy(c => c.Z);
        g.Ticks(30);
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, Cast(g, TestGame.CellCenter(top) + new Vector3(0, 1.5f, -0.4f), top), true));
        var west = block.Where(c => c.Y == ground.Y + 2).MinBy(c => c.X);
        g.Ticks(30);
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, Cast(g, TestGame.CellCenter(west) - new Vector3(1.5f, 0, 0), west), true));
        var built = Region(ground).Where(c => world.GetBlock(c) == wood).ToList();
        Assert.Equal(24, built.Count);
        Assert.All(built, c => Assert.True(BlockGrid.IsRaised(world, c)));

        // A pickaxe takes the first block out whole and leaves the ground alone.
        g.Hold("copper_pickaxe");
        var woodItem = g.Content.Items.Get("wood");
        int carried = g.Player.State.Inventory.Backpack.Count(woodItem);
        g.Ticks(30);
        g.AimAt(TestGame.CellCenter(block[0]));
        g.UseUntil(() => block.All(c => world.GetBlock(c) == 0));
        Assert.All(block, c => Assert.Equal(0, world.GetBlock(c)));
        Assert.Equal(stone, world.GetBlock(ground));
        Assert.Equal(16, Region(ground).Count(c => world.GetBlock(c) == wood));
        int dropped = g.World.Drops.Items.Where(d => d.Stack.Item == woodItem).Sum(d => d.Stack.Count);
        Assert.Equal(8, dropped + g.Player.State.Inventory.Backpack.Count(woodItem) - carried);
    }

    [Fact]
    public void BlocksOnGroundOnTheGridStayOnTheGrid()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var ground = GroundInFront(g, offGrid: false);
        g.Hold("wood", 20);
        g.AimAt(TestGame.CellCenter(ground) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), true));
        var block = BlockGrid.Cells(ground + Int3.Up).ToList();
        Assert.Equal(ground.Y + 1, block.Min(c => c.Y));
        Assert.All(block, c => Assert.Equal(g.Content.BlockId("wood"), world.GetBlock(c)));
        Assert.All(block, c => Assert.False(BlockGrid.IsRaised(world, c)));
    }

    [Fact]
    public void HoldingTheButtonNeverBuildsOntoItsOwnBlocks()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var wood = g.Content.BlockId("wood");
        var ground = GroundInFront(g, offGrid: false);
        g.Hold("wood", 50);
        g.AimAt(TestGame.CellCenter(ground) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        int held = g.Player.State.Inventory.Held.Count;

        // Still holding with the same aim: the cursor now rests on the new block, and nothing is built onto it.
        g.Ticks(30);
        var onNew = g.Target();
        Assert.Equal(wood, world.GetBlock(onNew!.Cell));
        Assert.Equal(UseOutcome.None, g.World.ItemUse.Use(g.Player, onNew, freshPress: false));
        Assert.Equal(held, g.Player.State.Inventory.Held.Count);

        // Sweeping the held cursor over to bare ground keeps building there.
        var side = ground + new Int3(4, 0, 0);
        g.AimAt(TestGame.CellCenter(side) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: false));

        // A new press builds onto them again.
        g.Ticks(30);
        g.AimAt(TestGame.CellCenter(ground) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
    }

    /// <summary>
    /// The top ground cell a few steps in front of the player, after raising a patch of the arena by one cell if needed
    /// so that the ground's surface is off the 1 m grid (or on it).
    /// </summary>
    private static Int3 GroundInFront(TestGame g, bool offGrid)
    {
        var top = g.Floor + new Int3(0, 0, 5);
        bool isOff = ((top.Y + 1) & 1) != 0;
        if (isOff == offGrid) return top;
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        foreach (var c in Region(top))
            if (c.Y == top.Y + 1) batch.SetBlock(c, stone);
        g.World.Session.Commit(batch);
        return top + Int3.Up;
    }

    /// <summary>The patch of arena in front of the player, from the ground up.</summary>
    private static IEnumerable<Int3> Region(Int3 ground)
    {
        for (int y = ground.Y; y <= ground.Y + 6; y++)
            for (int z = ground.Z - 3; z <= ground.Z + 4; z++)
                for (int x = ground.X - 6; x <= ground.X + 6; x++)
                    yield return new Int3(x, y, z);
    }

    /// <summary>Targets a cell from a point of view other than the player's eye.</summary>
    private static Target? Cast(TestGame g, Vector3 from, Int3 cell) =>
        Targeting.Cast(g.World.Session.World, from, Vector3.Normalize(TestGame.CellCenter(cell) - from), 20f);
}
