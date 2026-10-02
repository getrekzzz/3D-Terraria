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
}
