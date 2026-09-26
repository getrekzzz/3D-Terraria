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
        var clumps = Scatter((b, id) => Ground(b, id("turf")));
        float expected = 16f * 16f * Grass.ClumpsPerSquareMeter * Grass.Kinds["turf"].Density;
        Assert.InRange(clumps.Count, expected * 0.85f, expected * 1.15f);
        var look = Grass.Kinds["turf"];
        int turf = KindOf("turf");
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
    public void ScatterIsDeterministicAndShuffled()
    {
        var a = Scatter((b, id) => Ground(b, id("turf")));
        var b2 = Scatter((b, id) => Ground(b, id("turf")));
        Assert.Equal(a, b2);
        // Shuffled: the first tenth already spreads over the whole chunk (a prefix is an even thinning).
        var prefix = a.Take(a.Count / 10).ToList();
        Assert.Equal(16, prefix.Select(c => ((int)(c.Position.X / 4f), (int)(c.Position.Z / 4f))).Distinct().Count());
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
                    for (int y = 34; y < 50; y++) b.SetBlock(x, y, z, id("turf"));
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
            Ground(b, id("turf"), (x, _) => x < 48);
            Ground(b, id("stone"), (x, _) => x >= 48);
        });
        Assert.NotEmpty(border);
        Assert.All(border, c => Assert.True(c.Position.X < (48 - 32) * Units.CellSize + 0.5f, $"clump at x {c.Position.X} grows on stone"));

        var covered = Scatter((b, id) =>
        {
            Ground(b, id("turf"));
            for (int x = 36; x < 44; x++)
                for (int z = 36; z < 44; z++) b.SetBlock(x, Top, z, id("glass"));
        });
        float lo = (36 - 32) * Units.CellSize + 0.3f, hi = (44 - 32) * Units.CellSize - 0.3f;
        Assert.DoesNotContain(covered, c => c.Position.X > lo && c.Position.X < hi && c.Position.Z > lo && c.Position.Z < hi);
    }

    [Fact]
    public void JungleTurfUsesItsOwnKind()
    {
        var clumps = Scatter((b, id) => Ground(b, id("jungle_turf")));
        Assert.NotEmpty(clumps);
        Assert.All(clumps, c => Assert.Equal(KindOf("jungle_turf"), c.Kind));
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
                ["turf"] = new GrassKind { Color = "green", Height = new[] { 0.5f, 0.2f }, Flowers = 0.1f },
                ["stone"] = new GrassKind(),
            },
        };
        var problems = config.Validate(TestPaths.Content);
        Assert.Contains(problems, p => p.Contains("clumpsPerSquareMeter"));
        Assert.Contains(problems, p => p.Contains("turf: color"));
        Assert.Contains(problems, p => p.Contains("turf: height"));
        Assert.Contains(problems, p => p.Contains("turf: flowers need flowerColors"));
        Assert.Contains(problems, p => p.Contains("'stone' names a block without the 'grass' tag"));
        Assert.Contains(problems, p => p.Contains("'jungle_turf' is tagged 'grass' but has no grass kind"));
    }
}
