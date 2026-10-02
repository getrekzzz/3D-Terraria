using System.Diagnostics;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Index;
using Strata.Core.Gameplay.Guide;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class GuideIndexTests
{
    [Fact]
    public void SearchFindsPickaxesAndFiltersByCategory()
    {
        using var g = TestGame.Create();
        var index = GuideIndex.Build(g.Content);
        Assert.Contains(index.Search("pick"), i => i.Id == "copper_pickaxe");
        Assert.Contains(index.Search("copper pick"), i => i.Id == "copper_pickaxe");
        Assert.DoesNotContain(index.Search("pick", ItemCategory.Weapon), i => i.Id == "copper_pickaxe");
        Assert.Equal(index.Items.Count, index.Search("").Count);
    }

    [Fact]
    public void BuildsQuicklyOffTheMainThread()
    {
        using var g = TestGame.Create();
        GuideIndex.Build(g.Content); // warm up JIT and the content caches
        var watch = Stopwatch.StartNew();
        var index = GuideIndex.BuildAsync(g.Content).GetAwaiter().GetResult();
        watch.Stop();
        Assert.NotEmpty(index.Items);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void GelComesFromSlimesWithChances()
    {
        using var g = TestGame.Create();
        var index = GuideIndex.Build(g.Content);
        var gel = index.SourcesOf(g.Content.Items.Get("gel"));
        Assert.Contains(gel, s => s.Kind == ItemSourceKind.Drop && s.Link is EnemyDef e && e.HasTag("gel") && s.Detail.Contains('%'));
        Assert.True(index.SourcesOf(g.Content.Items.Get("torch")).Count >= 1);
        Assert.Equal("100%", GuideIndex.Percent(1f));
        Assert.Equal("33.33%", GuideIndex.Percent(1f / 3f));
        Assert.Equal("<0.01%", GuideIndex.Percent(0.00001f));
    }
}
