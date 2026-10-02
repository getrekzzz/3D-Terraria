using System.Text.RegularExpressions;
using Strata.Core.Content;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

/// <summary>
/// Generated assets must cover all content (regenerate with tools/blender/build_all.py, tools/textures/build_icons.py and
/// tools/textures/build_ui.py).
/// </summary>
public class AssetCoverageTests
{
    private static readonly Regex SpriteCall = new(@"UiSprites\.Get\(""([a-z0-9_]+)""\)", RegexOptions.Compiled);
    private static readonly Regex PreloadedList = new(@"Preloaded\s*=\s*\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"""([a-z0-9_]+)""", RegexOptions.Compiled);

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

    [Fact]
    public void EveryBuffHasAnIcon()
    {
        var missing = TestPaths.Content.Buffs.All.Where(b => !Exists(AssetPaths.BuffIcon(b))).Select(b => b.Id).ToList();
        Assert.True(missing.Count == 0, "buffs without icons (add a recipe to tools/textures/buff_icons.json, run build_ui.py): " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryEventWithAKillGoalHasAnIcon()
    {
        var missing = TestPaths.Content.Events.All.Where(e => e.KillGoal > 0 && !Exists(AssetPaths.EventIcon(e))).Select(e => e.Id).ToList();
        Assert.True(missing.Count == 0, "invasions without icons (add a recipe to tools/textures/event_icons.json, run build_ui.py): " + string.Join(", ", missing));
    }

    [Fact]
    public void UiSpritesUsedByCodeExist()
    {
        // The sprites the HUD and inventory are built on, plus every name game code asks UiSprites for.
        var names = new SortedSet<string>(StringComparer.Ordinal)
        {
            "heart", "heart_gold", "star", "bubble", "shield", "trash", "cursor", "sort", "quick_stack", "eye_open", "eye_closed",
            "slot_head", "slot_body", "slot_legs", "slot_accessory", "slot_dye", "slot_coin", "slot_ammo",
        };
        var game = Path.Combine(TestPaths.ProjectRoot, "game");
        foreach (var file in Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories))
        {
            var code = File.ReadAllText(file);
            foreach (Match m in SpriteCall.Matches(code)) names.Add(m.Groups[1].Value);
            if (Path.GetFileName(file) != "UiSprites.cs") continue;
            var list = PreloadedList.Match(code);
            Assert.True(list.Success, "UiSprites.Preloaded not found");
            foreach (Match m in Literal.Matches(list.Groups[1].Value)) names.Add(m.Groups[1].Value);
        }
        var missing = names.Where(n => !Exists(AssetPaths.UiSprite(n))).ToList();
        Assert.True(missing.Count == 0, "UI sprites without images (run tools/textures/build_ui.py): " + string.Join(", ", missing));
    }
}
