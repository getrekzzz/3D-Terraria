using Strata.Core.Content;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

/// <summary>Generated assets must cover all content (regenerate with tools/blender/build_all.py and tools/textures/build_icons.py).</summary>
public class AssetCoverageTests
{
    private static bool Exists(string rel) => File.Exists(Path.Combine(TestPaths.ProjectRoot, rel));

    [Fact]
    public void EveryItemHasAnIcon()
    {
        var missing = TestPaths.Content.Items.All.Where(i => !i.HasTag("placeholder") && !Exists(AssetPaths.ItemIcon(i))).Select(i => i.Id).ToList();
        Assert.True(missing.Count == 0, "items without icons: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryTileObjectVariantHasAModel()
    {
        var missing = new List<string>();
        foreach (var def in TestPaths.Content.TileObjects.All)
            for (int v = 0; v < def.Variants; v++)
                if (!Exists(AssetPaths.ObjectModel(def, v))) missing.Add($"{def.Id} v{v}");
        Assert.True(missing.Count == 0, "tile objects without models: " + string.Join(", ", missing));
    }

    [Fact]
    public void NonBlockItemsHaveModels()
    {
        var db = TestPaths.Content;
        var missing = db.Items.All.Where(i => !i.HasTag("placeholder")).Select(i => (i.Id, Path: AssetPaths.ItemModel(i, db))).Where(x => x.Path != null && !Exists(x.Path)).Select(x => x.Id).ToList();
        Assert.True(missing.Count == 0, "items without models: " + string.Join(", ", missing));
    }
}
