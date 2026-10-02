using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Presentation;

public class AmbienceRulesTests
{
    private static readonly AmbienceConfig Config = AmbienceConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    private static List<string> Loops(string[] biomes, WorldLayer layer, bool day)
    {
        var into = new List<AmbienceLoopRule>();
        AmbienceConfig.Select(Config.Loops, biomes, layer, day, into);
        return into.Select(r => r.Loop).ToList();
    }

    private static List<string> Particles(string[] biomes, WorldLayer layer, bool day)
    {
        var into = new List<AmbienceParticleRule>();
        AmbienceConfig.Select(Config.Particles, biomes, layer, day, into);
        return into.Select(r => r.Kind).ToList();
    }

    [Fact]
    public void ShippedConfigIsValid() => Assert.Empty(Config.Validate(TestPaths.Content));

    [Fact]
    public void SelectionFollowsBiomeLayerAndTime()
    {
        Assert.Equal(new[] { "forest_day" }, Loops(new[] { "forest" }, WorldLayer.Surface, day: true));
        Assert.Equal(new[] { "forest_night" }, Loops(new[] { "forest" }, WorldLayer.Surface, day: false));
        Assert.Contains("jungle_night", Loops(new[] { "jungle" }, WorldLayer.Surface, day: false));
        Assert.Contains("ocean", Loops(new[] { "ocean" }, WorldLayer.Surface, day: true));
        // Underground the surface beds give way to the cave's; a mushroom cave adds its hum.
        var cave = Loops(new[] { "forest", "mushroom" }, WorldLayer.Underground, day: true);
        Assert.Contains("cave", cave);
        Assert.Contains("mushroom", cave);
        Assert.DoesNotContain("forest_day", cave);
        Assert.Contains("cavern", Loops(new[] { "forest" }, WorldLayer.Cavern, day: false));
    }

    [Fact]
    public void ExclusiveRulesSilenceTheRest()
    {
        Assert.Equal(new[] { "underworld" }, Loops(new[] { "underworld", "corruption" }, WorldLayer.Underworld, day: true));
        Assert.Equal(new[] { "embers" }, Particles(new[] { "underworld" }, WorldLayer.Underworld, day: true));
    }

    [Fact]
    public void ParticlesFollowBiomeLayerAndTime()
    {
        Assert.Contains("fireflies", Particles(new[] { "forest" }, WorldLayer.Surface, day: false));
        Assert.DoesNotContain("fireflies", Particles(new[] { "forest" }, WorldLayer.Surface, day: true));
        Assert.Contains("leaves", Particles(new[] { "forest" }, WorldLayer.Surface, day: true));
        var cave = Particles(new[] { "mushroom" }, WorldLayer.Cavern, day: true);
        Assert.Contains("spores", cave);
        Assert.Contains("dust", cave);
        Assert.Contains("drips", cave);
        Assert.Empty(Particles(new[] { "desert" }, WorldLayer.Surface, day: true));
    }

    [Fact]
    public void PresetsScaleParticleCounts()
    {
        Assert.True(Config.ScaleFor("low") < Config.ScaleFor("medium"));
        Assert.True(Config.ScaleFor("ultra") > Config.ScaleFor("high"));
        Assert.Equal(Config.ScaleFor("medium"), Config.ScaleFor("custom"));
    }
}
