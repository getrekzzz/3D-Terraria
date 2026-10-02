using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class HudOverlaysTests
{
    private static readonly (float W, float H)[] Sizes = { (1024, 600), (1280, 720), (1920, 1080), (2560, 1440), (1066, 600) };

    private static readonly UiState[] States =
    {
        new(),
        new(BossBar: true, EventBar: true, BuffCount: 22, InfoLines: 6),
        new(NpcChat: true, BuffCount: 44, DebugOverlay: true),
        new(InventoryOpen: true),
        new(InventoryOpen: true, Panel: UiPanel.Chest, EventBar: true, BossBar: true),
    };

    [Fact]
    public void OverlaysNeverCoverARegionOrEachOther()
    {
        foreach (var (w, h) in Sizes)
            foreach (var s in States)
            {
                var layout = UiLayout.Compute(w, h, s);
                var captions = HudOverlays.CaptionsRect(layout, 5);
                var icon = HudOverlays.SaveIconRect(layout, captions);
                Assert.NotNull(icon);
                var screen = new UiRect(0, 0, w, h);
                foreach (var r in new[] { captions, icon })
                {
                    if (r is not { } rect) continue;
                    Assert.True(rect.Inside(screen), $"{w}x{h} {s}: {rect} off screen");
                    foreach (var (name, region) in layout.Rects)
                        Assert.False(rect.Intersects(region), $"{w}x{h} {s}: {rect} covers {name} {region}");
                }
                if (captions is { } c) Assert.False(c.Intersects(icon!.Value));
            }
    }

    [Fact]
    public void CaptionsSitJustAboveTheChatLog()
    {
        var layout = UiLayout.Compute(1920, 1080, new UiState());
        var chat = layout.Get(UiRegion.Chat);
        var captions = HudOverlays.CaptionsRect(layout, 5);
        Assert.NotNull(captions);
        Assert.Equal(chat.X, captions!.Value.X);
        Assert.Equal(chat.Y - UiLayout.Gap, captions.Value.Bottom, 2);
        Assert.Equal(5 * HudOverlays.CaptionLine, captions.Value.H, 2);
    }

    [Fact]
    public void TheSaveIconTakesTheBottomRightCornerWhenItIsFree()
    {
        var layout = UiLayout.Compute(1920, 1080, new UiState());
        var icon = HudOverlays.SaveIconRect(layout)!.Value;
        Assert.Equal(1920 - UiLayout.Margin, icon.Right, 2);
        Assert.Equal(1080 - UiLayout.Margin, icon.Bottom, 2);
        // The event bar holds that corner: the icon moves to the nearest free spot.
        var busy = UiLayout.Compute(1920, 1080, new UiState(EventBar: true));
        var moved = HudOverlays.SaveIconRect(busy)!.Value;
        Assert.False(moved.Intersects(busy.Get(UiRegion.EventBar)));
    }

    [Fact]
    public void TheIndicatorShowsForAtLeastItsMinimumThenFades()
    {
        var s = new SaveIndicator();
        s.Update(false, 0.1);
        Assert.False(s.Visible);
        s.Update(true, 0.05); // a save starts...
        Assert.True(s.Visible);
        s.Update(false, 0.2); // ...and is written at once: the icon stays
        for (int i = 0; i < 4; i++) s.Update(false, 0.2);
        Assert.Equal(1f, s.Alpha);
        float angle = s.Angle;
        for (int i = 0; i < 10; i++) s.Update(false, 0.1);
        Assert.False(s.Visible);
        Assert.NotEqual(0f, angle);
    }

    [Fact]
    public void TheIndicatorStaysWhileTheSaveRuns()
    {
        var s = new SaveIndicator();
        for (int i = 0; i < 50; i++) s.Update(true, 0.1);
        Assert.Equal(1f, s.Alpha);
        s.Update(false, 0.25);
        Assert.InRange(s.Alpha, 0.4f, 0.6f);
        s.Update(true, 0.05); // another save before it faded: back in
        Assert.True(s.Alpha > 0.5f);
    }
}
