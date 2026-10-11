using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class BiomeTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Liquids.Enabled = false;
        return g;
    }

    [Fact]
    public void ShimmerTakesCraftedItemsApart()
    {
        using var g = Arena();
        var cell = g.Floor + new Int3(4, 1, 0);
        g.World.Editor.FillLiquid(cell, cell + new Int3(0, 1, 0), g.Content.Liquids.Get("shimmer"), LiquidCode.MaxLevel);
        g.World.Drops.Spawn(new ItemStack(g.Content.Items.Get("wooden_chest"), 1), TestGame.CellCenter(cell), Vector3.Zero, pickupDelay: 600);
        g.Ticks(5);
        Assert.DoesNotContain(g.World.Drops.Items, d => d.Stack.Item!.Id == "wooden_chest");
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "wood" && d.Stack.Count == 8);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "iron_bar" && d.Stack.Count == 2);
    }

    [Fact]
    public void WadingIntoShimmerMakesYouPhase()
    {
        using var g = Arena();
        var feet = g.Player.FeetCell;
        g.World.Editor.FillLiquid(feet, feet + Int3.Up, g.Content.Liquids.Get("shimmer"), LiquidCode.MaxLevel);
        g.Ticks(2);
        Assert.True(g.Player.State.Buffs.Has("shimmering"));
        Assert.True(g.Player.State.Effects.Phasing);
    }

    [Fact]
    public void FiveTombstonesMakeAGraveyard()
    {
        using var g = Arena();
        var here = g.Player.FeetCell;
        Assert.DoesNotContain("graveyard", g.World.Session.ContextAt(here).Biomes);
        for (int i = 0; i < 5; i++)
            Assert.True(g.World.Session.World.TileObjects.TryAdd(new TileObjectInstance(g.Content.TileObjects.Get("tombstone"), g.Floor + new Int3(2 * i - 4, 1, 4), 0, 0)));
        g.Ticks(121); // the graveyard check is cached for two seconds
        Assert.Contains("graveyard", g.World.Session.ContextAt(here).Biomes);
    }

    [Fact]
    public void AFallenPlayerLeavesATombstone()
    {
        using var g = Arena();
        g.Player.State.Vitals.DamageDirect(10_000);
        g.Ticks(2);
        Assert.Contains(g.World.Session.World.TileObjects.InBox(new Aabb3i(g.Floor - Int3.One * 3, g.Floor + Int3.One * 6)), o => o.Def.Id == "tombstone");
    }

    [Fact]
    public void BelowTheJungleIsTheUndergroundJungleAndAboveEverythingIsSpace()
    {
        using var g = Arena();
        var session = g.World.Session;
        var jungle = session.FindSurfaceBiome("jungle", g.Player.FeetCell)!.Value;
        var deep = new Int3(jungle.X, session.Generator.Plan.Bands.CavernTop + 10, jungle.Z);
        var biomes = session.ContextAt(deep).Biomes;
        Assert.Contains("jungle", biomes);
        Assert.Contains("underground_jungle", biomes);
        var sky = new Int3(jungle.X, session.Generator.Plan.Bands.SpaceStart + 2, jungle.Z);
        Assert.Equal(WorldLayer.Space, session.ContextAt(sky).Layer);
        Assert.Contains("space", session.ContextAt(sky).Biomes);
    }

    [Fact]
    public void TheAetherHoldsAShimmerLake()
    {
        using var g = Arena();
        var session = g.World.Session;
        var aether = session.Generator.Plan.Features.All.OfType<Core.WorldGen.Features.ChamberFeature>().Single(f => f.Zone == "aether");
        var lake = new Int3((int)aether.Center.X, (int)(aether.Center.Y - aether.Radii.Y * 0.35f) - 2, (int)aether.Center.Z);
        var chunk = session.Generator.Generate(ChunkMath.CellToChunk(lake));
        var local = lake - ChunkMath.ChunkOrigin(ChunkMath.CellToChunk(lake));
        int liquid = chunk.Data.Liquids?[ChunkMath.Index(local)] ?? 0;
        Assert.Equal((int)LiquidType.Shimmer, liquid >> 5);
        Assert.Contains("aether", session.ContextAt(lake).Biomes);
    }

    [Fact]
    public void BrightBiomesHaveExposureBias()
    {
        Assert.True(TestPaths.Content.Biomes.Get("desert").ExposureBias < 1f);
        Assert.True(TestPaths.Content.Biomes.Get("tundra").ExposureBias < 1f);
        Assert.Equal(1f, TestPaths.Content.Biomes.Get("forest").ExposureBias);
        var db = ContentLoader.LoadFromTexts(new[] { ("biomes.json", """{ "biomes": [ { "id": "glare", "exposureBias": 3.0 } ] }""") });
        Assert.Contains(db.Report.Errors, e => e.Message.Contains("exposureBias", StringComparison.Ordinal));
    }

    [Fact]
    public void CarryingMilestoneItemsAdvancesTheWorld()
    {
        using var g = Arena();
        g.Ticks(60);
        Assert.True(g.World.Progression.IsSet("new_world"));
        Assert.False(g.World.Progression.IsSet("smelted_metal"));
        g.Player.State.Inventory.Pickup(new ItemStack(g.Content.Items.Get("tin_bar"), 3));
        g.Ticks(60);
        Assert.True(g.World.Progression.IsSet("smelted_metal"));
    }
}
