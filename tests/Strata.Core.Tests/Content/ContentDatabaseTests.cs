using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

public class ContentDatabaseTests
{
    [Fact]
    public void ShippedContentLoadsWithoutErrors()
    {
        var db = TestPaths.Content;
        Assert.False(db.Report.HasErrors, db.Report.ToString());
        Assert.True(db.Items.Count > 100);
        Assert.True(db.Blocks.Count > 50);
    }

    [Fact]
    public void AirIsAlwaysRuntimeIdZero()
    {
        Assert.Equal(0, TestPaths.Content.Blocks.Get("air").RuntimeId);
        Assert.Equal(ContentDatabase.Air, TestPaths.Content.BlockId("air"));
    }

    [Fact]
    public void PlaceableBlocksDeriveItems()
    {
        var db = TestPaths.Content;
        var stone = db.Items.Get("stone");
        Assert.Equal("stone", stone.PlaceBlock);
        Assert.Equal(ItemCategory.Block, stone.Category);
        Assert.Same(stone, db.BlockPlacers[db.BlockId("stone")]);
        // Grass drops loam, so it must not derive its own item.
        Assert.False(db.Items.Contains("grass"));
        Assert.Equal("dirt", db.BlockDrops[db.BlockId("grass")]!.Id);
    }

    [Fact]
    public void TileObjectsDerivePlacingItems()
    {
        var db = TestPaths.Content;
        Assert.Equal("work_bench", db.Items.Get("work_bench").PlaceObject);
        Assert.Equal("placed_bottle", db.Items.Get("bottle").PlaceObject);
        Assert.False(db.Items.Contains("oak_tree"));
    }

    [Fact]
    public void RuntimeIdsFollowRegistryOrder()
    {
        var db = TestPaths.Content;
        for (int i = 0; i < db.Items.Count; i++) Assert.Equal(i, db.Items[i].RuntimeId);
    }

    [Fact]
    public void ManifestMatchesDataDirectory()
    {
        var expected = ContentManifest.Serialize(ContentManifest.Build(Path.Combine(TestPaths.ProjectRoot, "data")));
        var actual = File.ReadAllText(Path.Combine(TestPaths.ProjectRoot, "data", "manifest.json"));
        Assert.Equal(expected, actual);
    }
}
