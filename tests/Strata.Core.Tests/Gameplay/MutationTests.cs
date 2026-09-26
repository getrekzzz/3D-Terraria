using Strata.Core.Common;
using Strata.Core.Diagnostics;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Mutations;

namespace Strata.Core.Tests.Gameplay;

public class MutationTests
{
    private static readonly RenderTables Tables = new(TestPaths.Content, _ => 0);

    private static TestGame Create(string? dir = null)
    {
        var g = TestGame.Create(dir);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        g.World.Spread.Enabled = false;
        return g;
    }

    private static void Hardmode(TestGame g) => g.World.Progression.Set("defeated_wall_of_flesh");

    /// <summary>Deep chunks below spawn (cavern stone, far from the test arena).</summary>
    private static IEnumerable<Int3> DeepChunks(TestGame g)
    {
        var bands = g.World.Session.Generator.Plan.Bands;
        var spawn = ChunkMath.CellToChunk(g.World.Session.SpawnCell);
        int y = ChunkMath.CellToChunk(new Int3(0, (int)bands.YForNormalizedDepth(0.75f), 0)).Y;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                yield return new Int3(spawn.X + dx, y, spawn.Z + dz);
    }

    private static int Count(ChunkData data, HashSet<ushort> blocks)
    {
        int n = 0;
        for (int i = 0; i < ChunkMath.Volume; i++)
            if (blocks.Contains(data.GetBlock(i))) n++;
        return n;
    }

    private static HashSet<ushort> HardmodeOres(TestGame g) =>
        g.World.Session.Mutations.Steps.OfType<OreVeinsStep>().Single().Rules.Select(r => r.Block).ToHashSet();

    [Fact]
    public void HardmodeAppendsItsMutationsOnceAndInOrder()
    {
        using var g = Create();
        var log = g.World.Session.Mutations;
        Assert.Equal(0, log.Level);
        Hardmode(g);
        Assert.Equal(new[] { "hardmode_ores", "hardmode_hallow", "hardmode_evil" }, log.Ids);
        g.World.Progression.Clear("hardmode");
        g.World.Progression.Set("hardmode");
        Assert.Equal(3, log.Level);
        // One ore per alternate group.
        Assert.Equal(3, g.World.Session.Mutations.Steps.OfType<OreVeinsStep>().Single().Rules.Count);
    }

    [Fact]
    public void ChunksProducedAfterHardmodeCarryTheNewOres()
    {
        using var g = Create();
        var session = g.World.Session;
        var chunks = DeepChunks(g).ToList();
        var before = chunks.ToDictionary(c => c, c => session.Produce(c));
        Hardmode(g);
        var ores = HardmodeOres(g);
        Assert.All(before.Values, p => Assert.Equal(0, Count(p.Data, ores)));
        int found = 0;
        foreach (var c in chunks)
        {
            var after = session.Produce(c);
            Assert.Equal(3, after.MutationLevel);
            found += Count(after.Data, ores);
        }
        Assert.True(found > 20, $"only {found} hardmode ore cells in {chunks.Count} deep chunks");
    }

    [Fact]
    public void LiveAndLazyApplicationGiveTheSameWorld()
    {
        using var live = Create();
        using var lazy = Create();
        var chunks = DeepChunks(live).ToList();
        foreach (var c in chunks) live.World.Session.Install(live.World.Session.Produce(c));
        Hardmode(live);
        Assert.True(live.World.Mutations.Behind > 0);
        live.World.Mutations.CatchUpAll();
        Assert.Equal(0, live.World.Mutations.Behind);

        Hardmode(lazy);
        foreach (var c in chunks)
        {
            var a = live.World.Session.World.GetChunk(c)!.Capture();
            var b = lazy.World.Session.Produce(c).Data;
            for (int i = 0; i < ChunkMath.Volume; i++)
                Assert.True(a.GetBlock(i) == b.GetBlock(i), $"chunk {c} differs at {i}");
        }
    }

    [Fact]
    public void SavedChunksCatchUpWhenTheyLoadAndKeepPlayerEdits()
    {
        var dir = Path.Combine(Path.GetTempPath(), "strata-mutations-" + Guid.NewGuid().ToString("N"));
        try
        {
            Int3 c;
            using (var probe = Create()) c = DeepChunks(probe).First();
            Int3 edited;
            ushort brick;
            using (var g = Create(dir))
            {
                var session = g.World.Session;
                session.Install(session.Produce(c));
                edited = ChunkMath.ChunkOrigin(c) + new Int3(5, 5, 5);
                brick = session.Content.BlockId("gray_brick");
                g.World.Editor.SetBlock(edited, brick, 0, CellFlags.PlayerPlaced);
                session.SaveAll(); // saved at mutation level 0
                Hardmode(g);
                session.SaveMetaOnly();
            }
            var reloaded = WorldSession.Load(TestPaths.Content, dir, Tables, new MemoryLog());
            Assert.Equal(3, reloaded.Mutations.Level);
            var product = reloaded.Produce(c);
            Assert.True(product.FromSave);
            Assert.Equal(3, product.MutationLevel);
            Assert.Equal(brick, product.Data.GetBlock(ChunkMath.Index(edited - ChunkMath.ChunkOrigin(c))));
            using var fresh = Create();
            Hardmode(fresh);
            var ores = HardmodeOres(fresh);
            Assert.Equal(Count(fresh.World.Session.Produce(c).Data, ores), Count(product.Data, ores));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WedgesConvertTheirSlabAndBecomeZones()
    {
        using var g = Create();
        Hardmode(g);
        var session = g.World.Session;
        var radiant = session.Mutations.Zones.OfType<WedgeStep>().Single(z => z.Zone == "hallow");
        Assert.Contains(session.Mutations.Zones, z => z.Zone is "corruption" or "crimson");
        var (x, z, _, _) = radiant.Anchor;
        var surface = new Int3((int)x, (int)MathF.Floor(session.Generator.Terrain.SurfaceHeight(x, z)), (int)z);
        Assert.Contains("hallow", session.ContextAt(surface).Biomes);
        var radiantBlocks = new[] { "hallowed_grass", "pearlstone", "pearlsand", "pink_ice" }.Select(session.Content.BlockId).ToHashSet();
        int converted = 0;
        for (int dy = -2; dy <= 0; dy++)
            converted += Count(session.Produce(ChunkMath.CellToChunk(surface) + new Int3(0, dy, 0)).Data, radiantBlocks);
        Assert.True(converted > 100, $"only {converted} radiant cells around the wedge's surface anchor");
        // Well away from the slab the land is untouched.
        var far = surface + new Int3((int)(radiant.Anchor.DirZ * 120), 0, (int)(-radiant.Anchor.DirX * 120));
        Assert.DoesNotContain("hallow", session.ZonesAt(far));
    }

    [Fact]
    public void BiomesSpreadOnlyOnceTheWorldHasChanged()
    {
        using var g = Create();
        var content = g.Content;
        var pearl = content.BlockId("pearlstone");
        g.World.Editor.SetBlock(g.Floor, pearl, 0, CellFlags.None);
        int CountPearl()
        {
            int n = 0;
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                    for (int dy = -2; dy <= 1; dy++)
                        if (g.World.Session.World.GetBlock(g.Floor + new Int3(dx, dy, dz)) == pearl) n++;
            return n;
        }
        for (int i = 0; i < 40; i++) g.World.Spread.Update(ChunkMath.Volume / 4);
        Assert.Equal(1, CountPearl());
        g.World.Progression.Set("hardmode");
        for (int i = 0; i < 40; i++) g.World.Spread.Update(ChunkMath.Volume / 4);
        Assert.True(CountPearl() > 1, "the pearlstone did not spread into the stone around it");
        Assert.True(g.World.Spread.Conversions > 0);
    }
}
