using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Meshing;

public class GrassScatterTests
{
    private static readonly FileSystemDataSource Data = new(TestPaths.ProjectRoot);
    private static readonly GrassConfig Grass = GrassConfig.Load(Data);
    private static readonly RenderTables Tables = new(TestPaths.Content, TextureLayerCatalog.Load(Data).IndexOf, Grass);

    // Chunk (1, 1, 1) spans cells 32–63; the ground's top face sits at cell height 40 (local y = 4 m).
    private const int Top = 40;
    private static readonly Int3 Chunk = new(1, 1, 1);

    private static List<GrassClump> Scatter(Action<EditBatch, Func<string, ushort>> edit)
    {
        var world = TestWorlds.Filled("air");
        var batch = world.BeginEdit();
        edit(batch, world.Content.BlockId);
        batch.Commit();
        var nb = Neighborhood.Capture(world, Chunk) ?? throw new InvalidOperationException("missing neighbours");
        return ChunkMesher.Build(nb, Tables, (_, _) => Top, Array.Empty<LightSource>()).Grass;
    }

    private static void Ground(EditBatch b, ushort block, Func<int, int, bool>? where = null)
    {
        for (int x = 0; x < 96; x++)
            for (int z = 0; z < 96; z++)
                if (where == null || where(x, z))
                    for (int y = Top - 4; y < Top; y++) b.SetBlock(x, y, z, block);
    }

    private static int KindOf(string block) =>
        Grass.Ordered.Select((k, i) => (k.Key, i)).First(k => k.Key == block).i;

    [Fact]
    public void FlatTurfGrowsAnEvenLawn()
    {
        var clumps = Scatter((b, id) => Ground(b, id("grass")));
        float expected = 16f * 16f * Grass.ClumpsPerSquareMeter * Grass.Kinds["grass"].Density;
        Assert.InRange(clumps.Count, expected * 0.85f, expected * 1.15f);
        var look = Grass.Kinds["grass"];
        int turf = KindOf("grass");
        foreach (var c in clumps)
        {
            Assert.InRange(c.Position.X, 0f, 16f);
            Assert.InRange(c.Position.Z, 0f, 16f);
            Assert.InRange(c.Position.Y, (Top - 32) * Units.CellSize - 0.3f, (Top - 32) * Units.CellSize + 0.3f);
            Assert.True(c.Normal.Y > 0.9f);
            Assert.Equal(turf, c.Kind);
            Assert.InRange(c.Height, look.MinHeight, look.MaxHeight);
            Assert.InRange(c.Flower, 0, look.FlowerColors.Length);
            Assert.True(c.Light >> 24 > 200, "open sky reaches the grass roots");
        }
        // Even cover: every 4 m square of the chunk has its share.
        var cells = clumps.GroupBy(c => ((int)(c.Position.X / 4f), (int)(c.Position.Z / 4f))).ToList();
        Assert.Equal(16, cells.Count);
        Assert.All(cells, g => Assert.InRange(g.Count(), clumps.Count / 16 * 0.6, clumps.Count / 16 * 1.4));
    }

    [Fact]
    public void ScatterIsDeterministicAndRankOrdered()
    {
        var a = Scatter((b, id) => Ground(b, id("grass")));
        var b2 = Scatter((b, id) => Ground(b, id("grass")));
        Assert.Equal(a, b2);
        Assert.All(a.Zip(a.Skip(1)), p => Assert.True(p.First.Rank <= p.Second.Rank));
        // Ranks are spread evenly: the first tenth already covers the whole chunk (a prefix is an even thinning).
        var prefix = a.Take(GrassScatter.VisibleCount(a, 0.1f)).ToList();
        Assert.InRange(prefix.Count, a.Count * 0.08, a.Count * 0.12);
        Assert.Equal(16, prefix.Select(c => ((int)(c.Position.X / 4f), (int)(c.Position.Z / 4f))).Distinct().Count());
        Assert.Equal(0, GrassScatter.VisibleCount(a, 0f));
        Assert.Equal(a.Count, GrassScatter.VisibleCount(a, 1f));
        Assert.Equal(a.Count(c => c.Rank < 0.35f), GrassScatter.VisibleCount(a, 0.35f));
    }

    [Fact]
    public void DiggingOnlyChangesTheGrassAtTheHole()
    {
        // A 1 m block dug out of a lawn. The renderer draws a rank prefix of each chunk (the medium and high presets
        // draw 35% and 70%), and the clumps it draws must stay put a short way from the hole, or the grass all over the
        // chunk flickers with every block mined. (The ground there settles by a fraction of a millimetre.)
        var lawn = Scatter((b, id) => Ground(b, id("grass")));
        var dug = Scatter((b, id) =>
        {
            Ground(b, id("grass"));
            for (int y = Top - 2; y < Top; y++)
                for (int z = 46; z < 48; z++)
                    for (int x = 46; x < 48; x++) b.SetBlock(x, y, z, 0);
        });
        var hole = new System.Numerics.Vector2((47 - 32) * Units.CellSize, (47 - 32) * Units.CellSize);
        bool Away(GrassClump c) => System.Numerics.Vector2.Distance(new(c.Position.X, c.Position.Z), hole) > 2.5f;
        Assert.True(lawn.Count(c => !Away(c)) > dug.Count(c => !Away(c)), "the hole takes its grass with it");
        foreach (float density in new[] { 0.35f, 0.7f, 1f })
        {
            var before = lawn.Take(GrassScatter.VisibleCount(lawn, density)).Where(Away).ToList();
            var after = dug.Take(GrassScatter.VisibleCount(dug, density)).Where(Away).ToList();
            Assert.NotEmpty(before);
            var gone = before.Where(c => !after.Any(o => Same(o, c))).ToList();
            var added = after.Where(c => !before.Any(o => Same(o, c))).ToList();
            Assert.True(gone.Count == 0 && added.Count == 0,
                $"at density {density}, {gone.Count} of {before.Count} clumps away from the hole vanished and {added.Count} appeared");
        }

        static bool Same(GrassClump a, GrassClump b) => a.Rank == b.Rank && System.Numerics.Vector3.Distance(a.Position, b.Position) < 1e-3f;
    }

    [Fact]
    public void StoneAndCliffsGrowNothing()
    {
        Assert.Empty(Scatter((b, id) => Ground(b, id("stone"))));
        // A turf wall: only its top grows grass, the sides stay bare.
        var wall = Scatter((b, id) =>
        {
            for (int x = 40; x < 44; x++)
                for (int z = 36; z < 60; z++)
                    for (int y = 34; y < 50; y++) b.SetBlock(x, y, z, id("grass"));
        });
        Assert.NotEmpty(wall);
        Assert.All(wall, c => Assert.True(c.Normal.Y >= Grass.MinUpward));
        Assert.All(wall, c => Assert.True(c.Position.Y > (49 - 32) * Units.CellSize));
    }

    [Fact]
    public void GrassStopsAtMaterialBordersAndUnderBlocks()
    {
        var border = Scatter((b, id) =>
        {
            Ground(b, id("grass"), (x, _) => x < 48);
            Ground(b, id("stone"), (x, _) => x >= 48);
        });
        Assert.NotEmpty(border);
        Assert.All(border, c => Assert.True(c.Position.X < (48 - 32) * Units.CellSize + 0.5f, $"clump at x {c.Position.X} grows on stone"));

        var covered = Scatter((b, id) =>
        {
            Ground(b, id("grass"));
            for (int x = 36; x < 44; x++)
                for (int z = 36; z < 44; z++) b.SetBlock(x, Top, z, id("glass"));
        });
        float lo = (36 - 32) * Units.CellSize + 0.3f, hi = (44 - 32) * Units.CellSize - 0.3f;
        Assert.DoesNotContain(covered, c => c.Position.X > lo && c.Position.X < hi && c.Position.Z > lo && c.Position.Z < hi);
    }

    [Fact]
    public void JungleTurfUsesItsOwnKind()
    {
        var clumps = Scatter((b, id) => Ground(b, id("jungle_grass")));
        Assert.NotEmpty(clumps);
        Assert.All(clumps, c => Assert.Equal(KindOf("jungle_grass"), c.Kind));
    }

    [Fact]
    public void ShippedGrassConfigIsValid() => Assert.Empty(Grass.Validate(TestPaths.Content));

    [Fact]
    public void GrassBlocksWithoutKindsAndBadValuesAreReported()
    {
        var config = new GrassConfig
        {
            ClumpsPerSquareMeter = 0f,
            Kinds = new()
            {
                ["grass"] = new GrassKind { Color = "green", Height = new[] { 0.5f, 0.2f }, Flowers = 0.1f },
                ["stone"] = new GrassKind(),
            },
        };
        var problems = config.Validate(TestPaths.Content);
        Assert.Contains(problems, p => p.Contains("clumpsPerSquareMeter"));
        Assert.Contains(problems, p => p.Contains("grass: color"));
        Assert.Contains(problems, p => p.Contains("grass: height"));
        Assert.Contains(problems, p => p.Contains("grass: flowers need flowerColors"));
        Assert.Contains(problems, p => p.Contains("'stone' names a block without the 'grass' tag"));
        Assert.Contains(problems, p => p.Contains("'jungle_grass' is tagged 'grass' but has no grass kind"));
    }
}
