using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Meshing;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

public class PaintTests
{
    private static int PaintAt(TestGame g, Int3 cell) => CellPaint.Of((byte)g.World.Session.World.GetFlags(cell.X, cell.Y, cell.Z));

    [Fact]
    public void ThePaintbrushPaintsAndTheScraperCleans()
    {
        using var g = TestGame.Create();
        var cell = g.Floor + new Int3(0, 0, 3);
        g.Hold("paintbrush");
        g.Player.State.Inventory.Backpack[4] = new ItemStack(g.Content.Items.Get("blue_paint"), 5);
        g.AimAt(TestGame.CellCenter(cell));
        Assert.Equal(UseOutcome.Painted, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal(g.Content.Items.Get("blue_paint").Paint, PaintAt(g, cell));
        Assert.Equal(4, g.Player.State.Inventory.Backpack[4].Count);

        g.Player.UseCooldown = 0;
        g.Hold("paint_scraper", slot: 1);
        Assert.Equal(UseOutcome.Painted, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal(0, PaintAt(g, cell));
    }

    [Fact]
    public void ThePaintSprayerPaintsPlacedBlocks()
    {
        using var g = TestGame.Create();
        g.Player.State.Inventory.Accessories[0] = new ItemStack(g.Content.Items.Get("paint_sprayer"), 1);
        g.Player.State.Recompute();
        g.Player.State.Inventory.Backpack[4] = new ItemStack(g.Content.Items.Get("red_paint"), 3);
        g.Hold("gray_brick", 10);
        var target = g.Floor + new Int3(0, 0, 3);
        g.AimAt(TestGame.CellCenter(target) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        var placed = target + Int3.Up;
        Assert.Equal(g.Content.BlockId("gray_brick"), g.World.Session.World.GetBlock(placed));
        Assert.Equal(g.Content.Items.Get("red_paint").Paint, PaintAt(g, placed));
    }

    [Fact]
    public void PaintReachesTheMesh()
    {
        using var g = TestGame.Create();
        var cell = g.Floor + new Int3(2, 1, 2);
        g.World.Editor.SetBlock(cell, g.Content.BlockId("gray_brick"));
        Assert.True(g.World.Editor.PaintBlock(cell, 7));
        var tables = new RenderTables(g.Content, _ => 0);
        var result = ChunkMesher.Build(Neighborhood.Capture(g.World.Session.World, ChunkMath.CellToChunk(cell))!, tables, (_, _) => int.MinValue, Array.Empty<Lighting.LightSource>());
        Assert.Contains(result.Opaque.Custom0, ids => (int)BitConverter.UInt16BitsToHalf((ushort)(ids >> 48)) == (MeshSurface.FlagCrisp | (7 << 1)));
    }
}
