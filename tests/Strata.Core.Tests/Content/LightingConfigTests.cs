using System.Text.Json;
using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

public class LightingConfigTests
{
    private static LightingConfig Shipped() => LightingConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void ShippedLightingIsValid()
    {
        var config = Shipped();
        Assert.Empty(config.Validate());
        Assert.Equal(GlowSettings.LevelCount, config.Glow.Levels.Length);
    }

    [Fact]
    public void OutOfRangeValuesAreReported()
    {
        var config = Shipped();
        config.Exposure.AutoMin = 0f;
        config.Exposure.AutoMax = 0.5f;
        config.Glow.Levels = new[] { 1f, 1f, 1f };
        config.Torch.PlacedEnergy = -1f;
        var problems = config.Validate();
        Assert.Contains(problems, p => p.StartsWith("exposure.autoMin", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("exposure.autoMax", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("glow.levels", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("torch.placedEnergy", StringComparison.Ordinal));
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void LeavingACaveMayNotBeSlowerThanEnteringOne()
    {
        var config = Shipped();
        config.Exposure.DarkenPerSecond = config.Exposure.BrightenPerSecond * 0.5f;
        Assert.Contains(config.Validate(), p => p.StartsWith("exposure.darkenPerSecond", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryValueMustBeInTheFile()
    {
        var source = new MemoryDataSource(LightingConfig.Path, """{ "exposure": { "caveBoost": 1.7 } }""");
        Assert.Throws<JsonException>(() => LightingConfig.Load(source));
    }

    private sealed class MemoryDataSource : IDataSource
    {
        private readonly string _path, _text;

        public MemoryDataSource(string path, string text) => (_path, _text) = (path, text);

        public bool Exists(string path) => path == _path;

        public string ReadText(string path) => path == _path ? _text : throw new FileNotFoundException(path);
    }
}
