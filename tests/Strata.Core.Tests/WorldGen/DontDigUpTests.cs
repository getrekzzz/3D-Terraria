using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Core.Tests.WorldGen;

/// <summary>Don't Dig Up (X-07): the upside-down world — the underworld on top, the surface at the bottom.</summary>
public class DontDigUpTests
{
    private static readonly Lazy<WorldGenerator> Ddu = new(() => Gen(SecretSeed.DontDigUp));

    private static WorldGenerator Gen(SecretSeed secret, long seed = 1234) =>
        new(TestPaths.Content, new WorldSettings { Seed = seed, Size = WorldSizePreset.Tiny, Secret = secret });

    private static (ushort Block, byte Liquid) Cell(WorldGenerator g, int x, int y, int z, Dictionary<Int3, GeneratedChunk> cache)
    {
        var coord = ChunkMath.CellToChunk(new Int3(x, y, z));
        if (!cache.TryGetValue(coord, out var chunk)) cache[coord] = chunk = g.Generate(coord);
        int i = ChunkMath.Index(ChunkMath.CellToLocal(new Int3(x, y, z)));
        return (chunk.Data.GetBlock(i), chunk.Data.Liquids?[i] ?? 0);
    }

    private static bool Solid(WorldGenerator g, ushort block) => g.Content.Blocks[block].IsSolid;

    [Fact]
    public void LayersRunUpsideDown()
    {
        var b = Ddu.Value.Plan.Bands;
        Assert.True(b.Inverted);
        Assert.True(0 < b.SurfaceFloor && b.SurfaceFloor < b.SeaLevel && b.SeaLevel < b.SkyStart && b.SkyStart < b.UndergroundTop
            && b.UndergroundTop < b.CavernTop && b.CavernTop < b.UnderworldTop && b.UnderworldTop < b.LavaLevel
            && b.LavaLevel < b.UnderworldCeiling && b.UnderworldCeiling < b.Height);
        Assert.Equal(WorldLayer.Underworld, b.LayerAt(b.Height - 1));
        Assert.Equal(WorldLayer.Cavern, b.LayerAt((b.CavernTop + b.UnderworldTop) / 2));
        Assert.Equal(WorldLayer.Underground, b.LayerAt(b.UndergroundTop + 2));
        Assert.Equal(WorldLayer.Sky, b.LayerAt(b.SkyStart + 2));
        Assert.Equal(WorldLayer.Surface, b.LayerAt(b.SeaLevel));
        Assert.Equal(WorldLayer.Underground, b.LayerAt(10));
        // Depth keeps its ordinary values at the layer boundaries and grows upwards above the surface's roof.
        var plain = new LayerBands(b.Height);
        Assert.Equal(plain.NormalizedDepth(plain.CavernTop), b.NormalizedDepth(b.CavernTop), 3);
        Assert.Equal(1f, b.NormalizedDepth(b.UnderworldTop), 3);
        Assert.True(b.NormalizedDepth(b.UnderworldTop + 20) > b.NormalizedDepth(b.CavernTop + 20));
        Assert.Equal(0f, b.NormalizedDepth(b.SeaLevel), 3);
        foreach (float d in new[] { 0.02f, 0.2f, 0.5f, 0.9f }) Assert.Equal(d, b.NormalizedDepth(b.YForNormalizedDepth(d)), 3);
        Assert.Equal(plain.Height * 93 / 100, plain.SpaceStart); // ordinary worlds keep their layout
        Assert.False(plain.Inverted);
    }

    [Fact]
    public void SpawnIsSafeAndStandableInTheUnderworldNearTheTop()
    {
        var g = Ddu.Value;
        var s = g.Plan.Spawn;
        var cache = new Dictionary<Int3, GeneratedChunk>();
        Assert.Equal(WorldLayer.Underworld, g.Plan.Bands.LayerAt(s.Y));
        Assert.True(s.Y > g.Plan.Bands.LavaLevel);
        Assert.True(Solid(g, Cell(g, s.X, s.Y - 1, s.Z, cache).Block));
        for (int h = 0; h < 6; h++)
        {
            var (block, liquid) = Cell(g, s.X, s.Y + h, s.Z, cache);
            Assert.False(Solid(g, block), $"headroom blocked {h} cells above spawn");
            Assert.Equal(0, liquid);
        }
        for (int dz = -8; dz <= 8; dz++)
            for (int dx = -8; dx <= 8; dx++)
                for (int dy = -3; dy <= 4; dy++)
                    Assert.Equal(0, Cell(g, s.X + dx, s.Y + dy, s.Z + dz, cache).Liquid);
    }

    [Fact]
    public void TheLavaSeaLiesUnderTheTop()
    {
        var g = Ddu.Value;
        var b = g.Plan.Bands;
        int lava = 0, lavaLow = 0;
        for (int cz = 1; cz < 15; cz += 3)
            for (int cx = 1; cx < 15; cx += 3)
                for (int cy = 0; cy < g.Plan.Size.Y / ChunkMath.Size; cy++)
                {
                    var c = g.Generate(new Int3(cx, cy, cz));
                    if (c.Data.Liquids == null) continue;
                    for (int i = 0; i < ChunkMath.Volume; i++)
                    {
                        if (LiquidCode.Type(c.Data.Liquids[i]) != LiquidType.Lava || c.Data.Liquids[i] == 0) continue;
                        int y = cy * ChunkMath.Size + (i >> (ChunkMath.Shift * 2));
                        if (y >= b.UnderworldTop) lava++;
                        else if (y < b.UndergroundTop) lavaLow++;
                    }
                }
        Assert.True(lava > 500, $"only {lava} lava cells in the top underworld");
        Assert.Equal(0, lavaLow); // nothing hot under the surface at the bottom
    }

    [Fact]
    public void GrassGrowsOnTheBottomSurfaceWithOpenAirAbove()
    {
        var g = Ddu.Value;
        var b = g.Plan.Bands;
        var cache = new Dictionary<Int3, GeneratedChunk>();
        int columns = 0, grass = 0, openAbove = 0;
        for (int z = 200; z < 320; z += 6)
            for (int x = 200; x < 320; x += 6)
            {
                int ground = -1;
                for (int y = b.UndergroundTop + 16; y > 3; y--)
                    if (Solid(g, Cell(g, x, y, z, cache).Block) && !Solid(g, Cell(g, x, y + 1, z, cache).Block)) { ground = y; break; }
                if (ground < 0 || ground > b.UndergroundTop) continue;
                float surface = g.Terrain.SurfaceHeight(x, z);
                if (MathF.Abs(ground - surface) > 4f) continue; // a cave floor under the roof
                columns++;
                var top = Cell(g, x, ground, z, cache).Block;
                if (g.Content.Blocks[top].Id.EndsWith("grass", StringComparison.Ordinal)) grass++;
                bool open = true;
                for (int y = ground + 1; y <= ground + 40 && open; y++) open = !Solid(g, Cell(g, x, y, z, cache).Block);
                if (open) openAbove++;
                Assert.True(Solid(g, Cell(g, x, (int)MathF.Ceiling(g.Terrain.CeilingHeight(x, z)) + 1, z, cache).Block)); // the rock roof
            }
        Assert.True(columns > 200, $"only {columns} surface columns found");
        Assert.True(openAbove > columns * 9 / 10, $"only {openAbove} of {columns} surface columns open to the air above");
        Assert.True(grass > columns / 3, $"{grass} of {columns} surface columns grassed");
        Assert.True(Solid(g, Cell(g, 256, 1, 256, cache).Block)); // the world's bottom stays solid
    }

    [Fact]
    public void TheDungeonStandsOnTheBottomSurface()
    {
        var g = Ddu.Value;
        var b = g.Plan.Bands;
        var dungeon = Assert.Single(g.Plan.Features.All.OfType<DungeonFeature>());
        var gate = g.Plan.Landmarks[LandmarkIds.DungeonGate];
        Assert.True(MathF.Abs(gate.Y - g.Terrain.SurfaceHeight(gate.X, gate.Z)) < 8f, $"gate {gate} off the surface");
        Assert.True(gate.Y < b.UndergroundTop);
        Assert.True(dungeon.Bounds.Min.Y >= 3, "the dungeon cuts the bedrock");
        Assert.True(dungeon.Layout.Rooms.Count >= DungeonBudget.For(WorldSizePreset.Tiny).MinRooms);
        foreach (var room in dungeon.Layout.Rooms) Assert.True(room.Interior.Min.Y >= DungeonBudget.InvertedFloor);
        Assert.True(g.Plan.Landmarks[LandmarkIds.DungeonBottom].Y < gate.Y);
    }

    [Fact]
    public void OrdinarySeedsAreUntouched()
    {
        var plain = Gen(SecretSeed.None);
        Assert.False(plain.Plan.Bands.Inverted);
        Assert.NotEqual(plain.Plan.Hash, Ddu.Value.Plan.Hash);
        Assert.True(float.IsPositiveInfinity(plain.Terrain.CeilingHeight(100, 100)));
        Assert.Equal(Gen(SecretSeed.None).Plan.Hash, plain.Plan.Hash);
    }

    [Fact]
    public void OlderDontDigUpWorldsKeepTheirOrdinaryLayout()
    {
        var ids = SecretSeeds.Ids(SecretSeed.DontDigUp | SecretSeed.Drunk);
        var old = new Strata.Core.Persistence.WorldMeta { Secrets = ids, GeneratorVersion = GenVersion.UpsideDownSince - 1 }.ToSettings();
        Assert.Equal(SecretSeed.Drunk, old.Secret);
        var current = new Strata.Core.Persistence.WorldMeta { Secrets = ids, GeneratorVersion = GenVersion.Current }.ToSettings();
        Assert.True(current.Has(SecretSeed.DontDigUp));
    }

    /// <summary>Golden Don't Dig Up chunks (seed 1234, tiny): the top underworld, the spawn, the bottom surface.</summary>
    public static readonly TheoryData<int, int, int, ulong> Golden = new()
    {
        { 8, 14, 8, 1192776085367922944UL }, // the underworld near the top
        { 5, 14, 7, 10067167907054985553UL }, // the spawn
        { 8, 3, 8, 6602206723201458421UL }, // the bottom surface
        { 9, 4, 9, 9417002913543057050UL }, // the bottom surface's open air under the roof
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void GoldenChunks(int x, int y, int z, ulong expected)
    {
        Assert.Equal(14, GenVersion.Current);
        Assert.Equal(new Int3(160, 448, 240), Ddu.Value.Plan.Spawn);
        Assert.Equal(expected, WorldGenTests.ChunkHash(Ddu.Value.Generate(new Int3(x, y, z))));
    }
}
