using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

public class GraphicsConfigTests
{
    private static GraphicsConfig Shipped() => GraphicsConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void ShippedPresetsAreValid()
    {
        var config = Shipped();
        Assert.Empty(config.Validate());
        Assert.Contains(config.Default, config.Presets.Keys);
        Assert.Equal(new[] { "low", "medium", "high", "ultra" }, config.Presets.Keys);
        // Even low gives the nearest placed torches a real light (shadowless omnis cost next to nothing in Forward+).
        Assert.True(config.Presets["low"].DynamicLights >= 4);
    }

    [Fact]
    public void PresetsRiseInQuality()
    {
        var p = Shipped().Presets.Values.ToList();
        for (int i = 1; i < p.Count; i++)
        {
            Assert.True(p[i].ShadowDistance >= p[i - 1].ShadowDistance);
            Assert.True(p[i].ShadowAtlas >= p[i - 1].ShadowAtlas);
            Assert.True(p[i].GrassDensity >= p[i - 1].GrassDensity);
            Assert.True(p[i].CloudSteps >= p[i - 1].CloudSteps);
            Assert.True(p[i].HorizonDistance >= p[i - 1].HorizonDistance);
        }
    }

    [Fact]
    public void OutOfRangeValuesAreReported()
    {
        var config = new GraphicsConfig
        {
            Default = "missing",
            Presets = new() { ["bad"] = new GraphicsPreset { Msaa = 3, ShadowAtlas = 5000, Upscaler = "dlss", SkyRadiance = 100, GrassDensity = 2f, GrassBuildsPerFrame = 17, HorizonDistance = 50f } },
        };
        var problems = config.Validate();
        Assert.Contains(problems, p => p.Contains("default preset"));
        Assert.Contains(problems, p => p.Contains("msaa"));
        Assert.Contains(problems, p => p.Contains("shadowAtlas"));
        Assert.Contains(problems, p => p.Contains("upscaler"));
        Assert.Contains(problems, p => p.Contains("skyRadiance"));
        Assert.Contains(problems, p => p.Contains("grassDensity"));
        Assert.Contains(problems, p => p.Contains("grassBuildsPerFrame"));
        Assert.Contains(problems, p => p.Contains("horizonDistance"));
    }

    [Fact]
    public void EffectBudgetsAreValidatedAndRiseWithThePreset()
    {
        Assert.Contains(new GraphicsPreset { EffectsScale = 2.5f }.Validate(), e => e.Contains("effectsScale"));
        Assert.Contains(new GraphicsPreset { FlashLights = 17 }.Validate(), e => e.Contains("flashLights"));
        Assert.Contains(new GraphicsPreset { ProjectileLights = 33 }.Validate(), e => e.Contains("projectileLights"));
        Assert.Contains(new GraphicsPreset { ProjectileLights = -1 }.Validate(), e => e.Contains("projectileLights"));
        Assert.DoesNotContain(new GraphicsPreset().Validate(), e => e.Contains("effectsScale") || e.Contains("flashLights"));
        var config = GraphicsConfig.Load(new Strata.Core.Content.Loading.FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.True(config.Get("low").EffectsScale < config.Get("ultra").EffectsScale);
        Assert.Equal(0, config.Get("low").FlashLights);
        Assert.Equal((0, 12), (config.Get("low").ProjectileLights, config.Get("ultra").ProjectileLights));
    }
}
