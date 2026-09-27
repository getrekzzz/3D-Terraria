using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Simulation.Liquids;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class LiquidTests
{
    private static LiquidDef Liquid(TestGame g, string id) => g.Content.Liquids.Get(id);

    /// <summary>Walls a square basin (interior half-width <paramref name="half"/>) on the arena floor around <paramref name="center"/>.</summary>
    private static void Basin(TestGame g, Int3 center, int half, int height)
    {
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        for (int y = 1; y <= height; y++)
            for (int d = -half - 1; d <= half + 1; d++)
            {
                batch.SetBlock(center.X + d, center.Y + y, center.Z - half - 1, stone);
                batch.SetBlock(center.X + d, center.Y + y, center.Z + half + 1, stone);
                batch.SetBlock(center.X - half - 1, center.Y + y, center.Z + d, stone);
                batch.SetBlock(center.X + half + 1, center.Y + y, center.Z + d, stone);
            }
        g.World.Session.Commit(batch);
    }

    private static IEnumerable<(Int3 Cell, byte Liquid)> LiquidIn(TestGame g, Int3 center, int half, int height)
    {
        var world = g.World.Session.World;
        for (int y = 1; y <= height; y++)
            for (int z = -half; z <= half; z++)
                for (int x = -half; x <= half; x++)
                {
                    var c = center + new Int3(x, y, z);
                    byte l = world.GetLiquid(c.X, c.Y, c.Z);
                    if (l != 0) yield return (c, l);
                }
    }

    private static int Volume(TestGame g, Int3 center, int half, int height, LiquidDef liquid) =>
        LiquidIn(g, center, half, height).Where(e => e.Liquid >> 5 == liquid.Code).Sum(e => e.Liquid & 0x1F);

    [Fact]
    public void PouredWaterFallsSpreadsAndSettles()
    {
        using var g = TestGame.Create();
        var water = Liquid(g, "water");
        var center = g.Floor + new Int3(6, 0, 0);
        Basin(g, center, 3, 6);
        int placed = g.World.Editor.PourLiquid(center + new Int3(0, 4, 0), water, 3 * LiquidCode.MaxLevel);
        Assert.Equal(93, placed);

        g.World.Liquids.Settle(600);

        Assert.Equal(0, g.World.Liquids.ActiveCells);
        var cells = LiquidIn(g, center, 3, 6).ToList();
        Assert.All(cells, e => Assert.Equal(center.Y + 1, e.Cell.Y)); // everything reached the floor
        Assert.True(cells.Count >= 13, $"water should spread out, covers {cells.Count} cells");
        int volume = Volume(g, center, 3, 6, water);
        Assert.InRange(volume, 93 - 12, 93); // only thin remnants may evaporate
        Assert.All(cells, e => Assert.True((e.Liquid & 0x1F) >= water.MinLevel - 1));
    }

    [Fact]
    public void FullBasinStaysAsleepAndLevelsAreEven()
    {
        using var g = TestGame.Create();
        var water = Liquid(g, "water");
        var center = g.Floor + new Int3(6, 0, 0);
        Basin(g, center, 2, 4);
        g.World.Editor.FillLiquid(center + new Int3(-2, 1, -2), center + new Int3(2, 2, 2), water, LiquidCode.MaxLevel);
        g.World.Liquids.Settle(50);
        Assert.Equal(0, g.World.Liquids.ActiveCells);
        Assert.Equal(50 * LiquidCode.MaxLevel, Volume(g, center, 2, 4, water));
    }

    [Fact]
    public void WaterMeetingLavaHardensIntoObsidian()
    {
        using var g = TestGame.Create();
        var center = g.Floor + new Int3(6, 0, 0);
        Basin(g, center, 2, 6);
        g.World.Editor.FillLiquid(center + new Int3(-2, 1, -2), center + new Int3(2, 1, 2), Liquid(g, "lava"), LiquidCode.MaxLevel);
        int mixed = 0;
        g.World.Liquids.Mixed += (_, _) => mixed++;
        g.World.Editor.PourLiquid(center + new Int3(0, 5, 0), Liquid(g, "water"), LiquidCode.MaxLevel);

        g.World.Liquids.Settle(600);

        Assert.True(mixed > 0);
        var world = g.World.Session.World;
        var obsidian = g.Content.BlockId("obsidian");
        Assert.Contains(Enumerable.Range(-2, 5), x => world.GetBlock(center + new Int3(x, 1, 0)) == obsidian || world.GetBlock(center + new Int3(0, 1, x)) == obsidian);
        // No water cell is left touching lava.
        foreach (var (cell, l) in LiquidIn(g, center, 2, 6).Where(e => e.Liquid >> 5 == (int)LiquidType.Water))
            foreach (var o in Faces.Offsets)
                Assert.NotEqual((int)LiquidType.Lava, world.GetLiquid(cell.X + o.X, cell.Y + o.Y, cell.Z + o.Z) >> 5);
    }

    [Fact]
    public void PlacedBlocksDisplaceLiquid()
    {
        using var g = TestGame.Create();
        var cell = g.Floor + new Int3(4, 1, 0);
        g.World.Liquids.Enabled = false;
        g.World.Editor.FillLiquid(cell, cell, Liquid(g, "water"), LiquidCode.MaxLevel);
        var stone = g.Content.Blocks.Get("stone");
        Assert.Equal(PlacementResult.Ok, g.World.Editor.PlaceBlock(cell, stone, 0, Array.Empty<(Vector3, Vector3)>()));
        Assert.Equal(0, g.World.Session.World.GetLiquid(cell.X, cell.Y, cell.Z));
    }

    [Fact]
    public void BucketsScoopAndPour()
    {
        using var g = TestGame.Create();
        var water = Liquid(g, "water");
        var center = g.Floor + new Int3(6, 0, 0);
        Basin(g, center, 2, 1);
        g.World.Editor.FillLiquid(center + new Int3(-2, 1, -2), center + new Int3(2, 1, 2), water, LiquidCode.MaxLevel);
        g.World.Liquids.Enabled = false;

        g.Hold("empty_bucket");
        g.AimAt(TestGame.CellCenter(center + new Int3(-1, 1, 0)));
        Assert.Equal(UseOutcome.CollectedLiquid, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.Equal("water_bucket", g.Player.State.Inventory.Held.Item!.Id);
        Assert.Equal(24 * LiquidCode.MaxLevel, Volume(g, center, 2, 1, water));

        g.Player.UseCooldown = 0;
        var target = g.Floor + new Int3(-4, 1, 0);
        g.AimAt(TestGame.CellCenter(target) - new Vector3(0, 0.2f, 0));
        Assert.Equal(UseOutcome.PouredLiquid, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.Equal("empty_bucket", g.Player.State.Inventory.Held.Item!.Id);
        Assert.Equal(LiquidCode.Full(LiquidType.Water), g.World.Session.World.GetLiquid(target.X, target.Y, target.Z));
    }

    [Fact]
    public void EmptyBucketNeedsEnoughLiquid()
    {
        using var g = TestGame.Create();
        var cell = g.Floor + new Int3(4, 1, 0);
        g.World.Liquids.Enabled = false;
        g.World.Editor.FillLiquid(cell, cell, Liquid(g, "water"), 6);
        g.Hold("empty_bucket");
        g.AimAt(TestGame.CellCenter(cell) - new Vector3(0, 0.2f, 0));
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.Equal("empty_bucket", g.Player.State.Inventory.Held.Item!.Id);
    }

    [Fact]
    public void LavaBurnsWaterDousesAndEmberskinProtects()
    {
        using var g = TestGame.Create();
        g.World.Liquids.Enabled = false;
        var feet = g.Player.FeetCell;
        var vitals = g.Player.State.Vitals;
        g.World.Editor.FillLiquid(feet, feet + Int3.Up, Liquid(g, "lava"), LiquidCode.MaxLevel);
        int life = vitals.Life;
        g.Ticks(1);
        Assert.Equal("lava", g.Player.Liquid?.Id);
        Assert.True(vitals.Life < life);
        Assert.True(g.Player.State.Buffs.Has("on_fire"));

        g.World.Editor.FillLiquid(feet, feet + Int3.Up, Liquid(g, "water"), LiquidCode.MaxLevel);
        g.Ticks(1);
        Assert.Equal("water", g.Player.Liquid?.Id);
        Assert.False(g.Player.State.Buffs.Has("on_fire"));

        g.Player.State.Buffs.Add(g.Content.Buffs.Get("obsidian_skin"), 3600);
        g.Player.State.Recompute();
        vitals.Life = vitals.MaxLife;
        vitals.ImmunityTicks = 0;
        g.World.Editor.FillLiquid(feet, feet + Int3.Up, Liquid(g, "lava"), LiquidCode.MaxLevel);
        g.Ticks(5);
        Assert.Equal(vitals.MaxLife, vitals.Life);
        Assert.False(g.Player.State.Buffs.Has("on_fire"));
    }

    [Fact]
    public void HoldingYourBreathRunsOutThenYouDrownUnlessYouHaveGills()
    {
        using var g = TestGame.Create();
        g.World.Liquids.Enabled = false;
        g.World.Spawner.Enabled = false;
        var feet = g.Player.FeetCell;
        g.World.Editor.FillLiquid(feet, feet + new Int3(0, 5, 0), Liquid(g, "water"), LiquidCode.MaxLevel);
        var vitals = g.Player.State.Vitals;
        g.Ticks(PlayerEntity.MaxBreath - 10);
        Assert.True(g.Player.Submerged);
        Assert.InRange(g.Player.Breath, 1, 20);
        Assert.Equal(vitals.MaxLife, vitals.Life);
        g.Ticks(70);
        Assert.Equal(0, g.Player.Breath);
        Assert.True(vitals.Life < vitals.MaxLife);

        vitals.Life = vitals.MaxLife;
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("gills"), 7200);
        g.Player.State.Recompute();
        g.Ticks(120);
        Assert.Equal(vitals.MaxLife, vitals.Life);
        Assert.True(g.Player.Breath > 0); // refills while breathing water

        g.World.Editor.FillLiquid(feet, feet + new Int3(0, 5, 0), null, 0);
        g.Ticks(300);
        Assert.Equal(PlayerEntity.MaxBreath, g.Player.Breath);
    }

    [Fact]
    public void LavaDestroysDroppedItemsExceptLiquidProofOnes()
    {
        using var g = TestGame.Create();
        g.World.Liquids.Enabled = false;
        var cell = g.Floor + new Int3(8, 1, 8);
        g.World.Editor.FillLiquid(cell, cell, Liquid(g, "lava"), LiquidCode.MaxLevel);
        var wood = g.World.Drops.Spawn(new ItemStack(g.Content.Items.Get("wood"), 5), TestGame.CellCenter(cell), Vector3.Zero, pickupDelay: 600);
        var bucket = g.World.Drops.Spawn(new ItemStack(g.Content.Items.Get("lava_bucket"), 1), TestGame.CellCenter(cell), Vector3.Zero, pickupDelay: 600);
        g.Ticks(2);
        Assert.DoesNotContain(wood, g.World.Drops.Items);
        Assert.Contains(bucket, g.World.Drops.Items);
    }

    [Fact]
    public void LoadingAChunkWakesOnlyLiquidThatCanMove()
    {
        using var g = TestGame.Create();
        var session = g.World.Session;
        g.World.Liquids.Enabled = false;
        var hanging = g.Floor + new Int3(3, 6, 3);
        var pooled = g.Floor + new Int3(-3, 1, -3);
        Basin(g, pooled - Int3.Up, 0, 1);
        g.World.Editor.FillLiquid(hanging, hanging, Liquid(g, "water"), LiquidCode.MaxLevel);
        g.World.Editor.FillLiquid(pooled, pooled, Liquid(g, "water"), LiquidCode.MaxLevel);

        // A fresh simulator only learns about liquid through chunk loads.
        var sim = new LiquidSimulator(session);
        foreach (var coord in new[] { ChunkMath.CellToChunk(hanging), ChunkMath.CellToChunk(pooled) }.Distinct())
        {
            var chunk = session.World.GetChunk(coord)!;
            session.Install(new ChunkProduct(coord, chunk.Capture(), Array.Empty<TileObjectInstance>(), chunk.MutationLevel, FromSave: true));
        }
        Assert.True(sim.IsActive(hanging));
        Assert.False(sim.IsActive(pooled));
    }

    [Fact]
    public void FreshlyGeneratedChunksDoNotWakeTheirLiquid()
    {
        using var g = TestGame.Create();
        var session = g.World.Session;
        g.World.Liquids.Enabled = false;
        var hanging = g.Floor + new Int3(3, 6, 3);
        g.World.Editor.FillLiquid(hanging, hanging, Liquid(g, "water"), LiquidCode.MaxLevel);
        var sim = new LiquidSimulator(session);
        var coord = ChunkMath.CellToChunk(hanging);
        var chunk = session.World.GetChunk(coord)!;
        session.Install(new ChunkProduct(coord, chunk.Capture(), Array.Empty<TileObjectInstance>(), chunk.MutationLevel, FromSave: false));
        Assert.False(sim.IsActive(hanging)); // generated liquid is static until disturbed
        g.World.Editor.SetBlock(hanging + new Int3(1, 0, 0), g.Content.BlockId("stone")); // an edit next to it wakes it
        Assert.True(sim.IsActive(hanging));
    }

    [Fact]
    public void GeneratedLiquidNeverHangsOverOpenSpaceInsideAChunk()
    {
        using var g = TestGame.Create();
        var session = g.World.Session;
        var spawn = ChunkMath.CellToChunk(session.SpawnCell);
        int checkedCells = 0;
        for (int cy = 0; cy < session.World.ChunkCounts.Y; cy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var coord = new Int3(spawn.X + dx, cy, spawn.Z + dz);
                    var data = session.Generator.Generate(coord).Data;
                    if (data.Liquids == null) continue;
                    for (int ly = 1; ly < ChunkMath.Size; ly++)
                        for (int lz = 0; lz < ChunkMath.Size; lz++)
                            for (int lx = 0; lx < ChunkMath.Size; lx++)
                            {
                                int i = ChunkMath.Index(lx, ly, lz), below = ChunkMath.Index(lx, ly - 1, lz);
                                if (data.Liquids[i] == 0) continue;
                                checkedCells++;
                                bool open = !g.Content.Blocks[data.GetBlock(below)].IsSolid && data.Liquids[below] == 0;
                                Assert.False(open, $"liquid hangs at {coord * ChunkMath.Size + new Int3(lx, ly, lz)}");
                            }
                }
        Assert.True(checkedCells > 0);
    }

    [Fact]
    public void LiquidEditsDirtyNeighbourChunksAtBordersAndLavaRelights()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var chunk = ChunkMath.CellToChunk(g.Floor);
        var border = chunk * ChunkMath.Size + new Int3(ChunkMath.Size - 1, 5, 5);
        var batch = world.BeginEdit();
        batch.SetLiquid(border, LiquidCode.Full(LiquidType.Water));
        var result = batch.Commit();
        Assert.Contains(chunk + new Int3(1, 0, 0), result.AffectedChunks);

        var inner = chunk * ChunkMath.Size + new Int3(16, 16, 16);
        batch = world.BeginEdit();
        batch.SetLiquid(inner, LiquidCode.Full(LiquidType.Water));
        Assert.Single(batch.Commit().AffectedChunks);

        batch = world.BeginEdit();
        batch.SetLiquid(inner + Int3.Up, LiquidCode.Full(LiquidType.Lava));
        Assert.True(batch.Commit().AffectedChunks.Count > 1); // lava light reaches neighbours

        var tables = new RenderTables(g.Content, _ => 0);
        Assert.NotEqual(0, tables.LiquidEmission[(int)LiquidType.Lava]);
        Assert.Equal(0, tables.LiquidEmission[(int)LiquidType.Water]);
    }
}
