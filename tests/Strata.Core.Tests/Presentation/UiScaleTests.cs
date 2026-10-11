using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class UiScaleTests
{
    [Theory]
    [InlineData(1f, 1600, 900, 1f)]
    [InlineData(1.5f, 1600, 900, 1.5f)]
    [InlineData(2f, 1600, 900, 1.5f)] //   900 / 600
    [InlineData(1.5f, 1280, 720, 1.2f)] // 720 / 600
    [InlineData(2f, 1920, 1080, 1.8f)]
    [InlineData(2f, 3840, 2160, 2f)]
    [InlineData(1f, 800, 600, 0.78125f)] // 800 / 1024
    [InlineData(1f, 300, 200, 0.5f)] //    never below the minimum scale
    [InlineData(5f, 0, 0, 3f)] //          unknown window: the setting, clamped
    [InlineData(1.25f, 0, 0, 1.25f)]
    public void TheSettingIsLoweredToFitTheWindow(float setting, int width, int height, float expected)
    {
        Assert.Equal(expected, UiScale.Effective(setting, width, height), 4);
    }

    [Fact]
    public void TheLogicalViewportNeverDropsBelowTheLayoutMinimum()
    {
        foreach (var (w, h) in new[] { (1024, 600), (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3440, 1440) })
            for (float setting = 0.75f; setting <= 3f; setting += 0.05f)
            {
                float scale = UiScale.Effective(setting, w, h);
                Assert.True(scale <= setting + 1e-5f);
                Assert.True(w / scale >= UiScale.MinLogicalWidth - 0.01f, $"{w}x{h} at {setting}: width {w / scale}");
                Assert.True(h / scale >= UiScale.MinLogicalHeight - 0.01f, $"{w}x{h} at {setting}: height {h / scale}");
            }
    }
}
