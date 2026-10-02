using Strata.Core.Meshing;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

/// <summary>World ores glow softly (shader only) so veins read in a dark cave; nothing else glows.</summary>
public class OreGlowTests
{
    [Fact]
    public void EveryOreBlockGlowsWithinRange()
    {
        var ores = TestPaths.Content.Blocks.All.Where(b => b.HasTag("ore")).ToList();
        Assert.NotEmpty(ores);
        var missing = ores.Where(b => b.Glow is not (> 0f and <= 1f)).Select(b => b.Id).ToList();
        Assert.True(missing.Count == 0, "ores without a glow in (0, 1]: " + string.Join(", ", missing));
    }

    [Fact]
    public void NonOreBlocksDoNotGlow()
    {
        var glowing = TestPaths.Content.Blocks.All.Where(b => !b.HasTag("ore") && b.Glow != null).Select(b => b.Id).ToList();
        Assert.True(glowing.Count == 0, "non-ore blocks with a glow: " + string.Join(", ", glowing));
    }

    [Fact]
    public void CommonMetalOresGlowFaintlyAndRareOnesMore()
    {
        var blocks = TestPaths.Content.Blocks;
        foreach (var id in new[] { "copper_ore", "tin_ore", "iron_ore", "lead_ore", "silver_ore", "tungsten_ore", "gold_ore", "platinum_ore" })
            Assert.InRange(blocks.Get(id).Glow!.Value, 0.05f, 0.2f);
        foreach (var id in new[] { "demonite_ore", "crimtane_ore", "hellstone", "cobalt_ore", "adamantite_ore", "luminite" })
            Assert.True(blocks.Get(id).Glow > 0.2f, id);
    }

    [Fact]
    public void GlowReachesTheRenderTablesButNotTheBakedLight()
    {
        var content = TestPaths.Content;
        var tables = new RenderTables(content, _ => 0);
        var copper = content.Blocks.Get("copper_ore");
        var stone = content.Blocks.Get("stone");
        Assert.Equal(copper.Glow!.Value, tables.SurfaceGlow[tables.Surface[copper.RuntimeId]]);
        Assert.Equal(0f, tables.SurfaceGlow[tables.Surface[stone.RuntimeId]]);
        Assert.Equal(0, tables.Emission[copper.RuntimeId]); // copper has no light: its glow lights nothing
    }
}
