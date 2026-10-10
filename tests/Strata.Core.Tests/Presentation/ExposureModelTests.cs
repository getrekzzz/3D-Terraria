using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class ExposureModelTests
{
    private const float Dt = 1f / 60f;

    /// <summary>The starting values from data/config/lighting.json, fixed here so retuning the file does not move the model's tests.</summary>
    private static ExposureSettings Settings() => new()
    {
        CaveBoost = 1.7f, NightBoost = 1.2f, BrightenPerSecond = 0.5f, DarkenPerSecond = 2.5f,
        AutoKey = 0.35f, AutoMin = 0.72f, AutoMax = 1.45f, AutoSpeed = 1f,
    };

    [Fact]
    public void DesertNoonIsDarkerThanForest()
    {
        var s = Settings();
        Assert.Equal(1f, ExposureModel.Target(s, 1f, 1f, 1f), 5);
        Assert.True(ExposureModel.Target(s, 1f, 1f, 0.85f) < ExposureModel.Target(s, 1f, 1f, 1f));
        Assert.Equal(0.85f, ExposureModel.Target(s, 1f, 1f, 0.85f), 5);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0.85f)]
    [InlineData(0.5f, 0.82f)]
    [InlineData(0f, 1.5f)]
    public void SealedCaveAdapts(float daylight, float bias)
    {
        var s = Settings();
        Assert.Equal(s.CaveBoost, ExposureModel.Target(s, 0f, daylight, bias), 5);
    }

    [Fact]
    public void NightOutdoorsUsesTheNightBoostWhateverTheBiome()
    {
        var s = Settings();
        Assert.Equal(s.NightBoost, ExposureModel.Target(s, 1f, 0f, 0.85f), 5);
        Assert.Equal(s.NightBoost, ExposureModel.Target(s, 1f, 0f, 1f), 5);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(1f, 0.82f)]
    [InlineData(0f, 0.85f)]
    [InlineData(0.4f, 1.2f)]
    public void MonotoneInOutdoor(float daylight, float bias)
    {
        var s = Settings();
        float previous = float.MaxValue;
        for (int i = 0; i <= 10; i++)
        {
            float target = ExposureModel.Target(s, i * 0.1f, daylight, bias);
            Assert.True(target <= previous + 1e-6f, $"target rose at outdoor {i * 0.1f}: {target} > {previous}");
            previous = target;
        }
    }

    [Fact]
    public void LeavingACaveDarkensFastEnteringBrightensSlowly()
    {
        var s = Settings();
        var model = new ExposureModel(s) { Current = 1.7f };
        float t = 0f;
        while (MathF.Abs(model.Current - 0.85f) > 0.085f && t < 5f)
        {
            model.Step(0.85f, Dt);
            t += Dt;
        }
        Assert.True(t <= 1.0f, $"took {t:0.00} s to come within 10% of the outdoor exposure");
        Assert.True(model.Current >= 0.85f, "never undershoots");

        model.Current = 0.85f;
        for (int i = 0; i < 120; i++) model.Step(1.7f, Dt);
        Assert.True(model.Current < 0.9f * 1.7f, $"reached {model.Current} after 2 s: brightening must be gradual");
        Assert.True(model.Current > 0.85f);
    }

    [Fact]
    public void StepIsIndependentOfTheFrameRate()
    {
        var a = new ExposureModel(Settings()) { Current = 1.7f };
        var b = new ExposureModel(Settings()) { Current = 1.7f };
        for (int i = 0; i < 60; i++) a.Step(0.85f, Dt);
        for (int i = 0; i < 4; i++) b.Step(0.85f, 0.25f);
        Assert.Equal(a.Current, b.Current, 3);
        b.Step(0.85f, 0f);
        Assert.Equal(a.Current, b.Current, 3);
    }

    [Fact]
    public void AutoWindowMatchesGodotConversion()
    {
        var (min, max) = ExposureModel.AutoWindow(Settings());
        Assert.InRange(min, 193.0f, 193.2f);
        Assert.InRange(max, 388.8f, 389.0f);
        // Back through Godot's conversion: multiplier = key / (ISO * 12.5 / 100 / 100).
        Assert.Equal(1.45f, 0.35f / (min * 12.5f / 100f / 100f), 3);
        Assert.Equal(0.72f, 0.35f / (max * 12.5f / 100f / 100f), 3);
    }
}
