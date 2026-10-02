using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class UiLayoutTests
{
    private static readonly (int W, int H)[] Windows =
    {
        (1024, 600), (1280, 720), (1280, 800), (1366, 768), (1440, 900), (1600, 900), (1680, 1050), (1920, 1080),
        (1920, 1200), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160),
    };

    /// <summary>Every step of the UI scale slider, 0.75–2.</summary>
    private static readonly float[] UiScales = Enumerable.Range(0, 26).Select(i => 0.75f + i * 0.05f).ToArray();

    private static readonly UiState Hud = new(InfoLines: 3);
    private static readonly UiState Crowded = new(BossBar: true, EventBar: true, NpcChat: true, BuffCount: 44, InfoLines: 6);
    private static readonly UiState Inventory = new(InventoryOpen: true, BuffCount: 5, InfoLines: 3);
    private static readonly UiState Chest = Inventory with { Panel = UiPanel.Chest };
    private static readonly UiState Shop = Inventory with { Panel = UiPanel.Shop };
    private static readonly UiState Service = Inventory with { Panel = UiPanel.Service, AccessoryRows = 7 };

    private static readonly UiState[] States =
    {
        Hud, Crowded, Inventory, Chest, Shop, Service,
        Hud with { DebugOverlay = true }, Crowded with { DebugOverlay = true }, Chest with { DebugOverlay = true },
        Hud with { BuffCount = 11, BossBar = true, DebugOverlay = true }, Hud with { NpcChat = true, BuffCount = 3 },
        Hud with { ShowMinimap = false, BossBar = true, EventBar = true },
        Crowded with { InventoryOpen = true, Panel = UiPanel.Chest, AccessoryRows = 7 },
    };

    /// <summary>Every window from 1024×600 to 3840×2160 at UI scales 0.75–2, as the logical viewport the engine lays out.</summary>
    public static IEnumerable<(float W, float H, string Name)> Viewports()
    {
        foreach (var (w, h) in Windows)
            foreach (float setting in UiScales)
            {
                float scale = UiScale.Effective(setting, w, h);
                yield return (w / scale, h / scale, $"{w}x{h} @{setting}");
            }
    }

    [Fact]
    public void NoVisibleRegionsOverlapOrLeaveTheScreen()
    {
        foreach (var (w, h, name) in Viewports())
            foreach (var s in States)
            {
                var layout = UiLayout.Compute(w, h, s);
                var screen = new UiRect(0, 0, w, h);
                var rects = layout.Rects.ToArray();
                for (int i = 0; i < rects.Length; i++)
                {
                    Assert.False(rects[i].Value.IsEmpty, $"{name} {s}: {rects[i].Key} is empty");
                    Assert.True(rects[i].Value.Inside(screen), $"{name} {s}: {rects[i].Key} {rects[i].Value} leaves the screen");
                    for (int j = i + 1; j < rects.Length; j++)
                        Assert.False(rects[i].Value.Intersects(rects[j].Value),
                            $"{name} {s}: {rects[i].Key} {rects[i].Value} overlaps {rects[j].Key} {rects[j].Value}");
                }
                foreach (string region in Expected(s))
                    Assert.True(layout.Has(region), $"{name} {s}: {region} is missing");
            }
    }

    [Fact]
    public void HotbarAndInventoryRowShareOrigin()
    {
        foreach (var (w, h, name) in Viewports())
        {
            var hud = UiLayout.Compute(w, h, Hud).Get(UiRegion.Hotbar);
            foreach (var s in new[] { Inventory, Chest, Shop, Service })
            {
                var layout = UiLayout.Compute(w, h, s);
                var grid = layout.Get(UiRegion.Inventory);
                Assert.True(hud.X == grid.X && hud.Y == grid.Y, $"{name} {s.Panel}: hotbar {hud}, inventory {grid}");
                Assert.Equal(10 * UiLayout.Slot * layout.SlotScale + 9 * UiLayout.Gap, grid.W, 3);
                Assert.False(layout.Has(UiRegion.Hotbar)); // the inventory's top row is the hotbar
            }
        }
    }

    [Fact]
    public void MinimapHiddenOnlyWhenEquipmentCannotFit()
    {
        foreach (var (w, h, name) in Viewports())
            foreach (var s in States)
            {
                var layout = UiLayout.Compute(w, h, s);
                float underMinimap = layout.Get(UiRegion.Hearts).Bottom + UiLayout.Gap + UiLayout.MinimapSize + UiLayout.Gap
                    + UiLayout.EquipmentHeight(s.AccessoryRows, layout.SlotScale);
                if (layout.MinimapHidden)
                {
                    Assert.True(s.InventoryOpen && s.ShowMinimap, $"{name} {s}: minimap hidden outside the inventory");
                    Assert.True(underMinimap > h - UiLayout.Margin, $"{name} {s}: the equipment fit under the minimap");
                    Assert.False(layout.Has(UiRegion.Minimap));
                }
                else if (s.ShowMinimap)
                {
                    Assert.True(layout.Has(UiRegion.Minimap), $"{name} {s}: minimap missing");
                    if (s.InventoryOpen)
                        Assert.True(layout.Get(UiRegion.Equipment).Y >= layout.Get(UiRegion.Minimap).Bottom, $"{name} {s}");
                }
            }
        Assert.False(UiLayout.Compute(1920, 1080, Inventory).MinimapHidden);
        Assert.True(UiLayout.Compute(1024, 600, Service).MinimapHidden);
    }

    [Fact]
    public void NpcChatNeverOverlapsHotbarOrHearts()
    {
        foreach (var (w, h, name) in Viewports())
            foreach (int buffs in new[] { 0, 5, 11, 22, 44 })
                foreach (bool bars in new[] { false, true })
                {
                    var s = new UiState(NpcChat: true, BuffCount: buffs, BossBar: bars, EventBar: bars, InfoLines: 4);
                    var layout = UiLayout.Compute(w, h, s);
                    Assert.True(layout.TryGet(UiRegion.NpcChat, out var chat), $"{name} {s}: no NPC chat");
                    foreach (string other in new[] { UiRegion.Hotbar, UiRegion.ItemName, UiRegion.Hearts, UiRegion.LifeText,
                                 UiRegion.Mana, UiRegion.Buffs, UiRegion.Minimap })
                        Assert.False(chat.Intersects(layout.Get(other)), $"{name} {s}: NPC chat {chat} covers {other}");
                    var hotbar = layout.Get(UiRegion.Hotbar);
                    var hearts = layout.Get(UiRegion.Hearts);
                    bool gap = hearts.X - hotbar.Right - 2 * UiLayout.NpcChatClearance >= UiLayout.NpcChatMinWidth;
                    if (gap)
                    {
                        // the top centre, clear of the hotbar and the hearts
                        Assert.Equal(UiLayout.HotbarY, chat.Y);
                        Assert.True(chat.X >= hotbar.Right + UiLayout.NpcChatClearance, $"{name}: {chat} too close to the hotbar");
                        Assert.True(chat.Right <= hearts.X - UiLayout.NpcChatClearance, $"{name}: {chat} too close to the hearts");
                    }
                    else
                        Assert.True(chat.Y > layout.Get(buffs > 0 ? UiRegion.Buffs : UiRegion.Hotbar).Bottom, $"{name} {s}: {chat}");
                    if (buffs <= UiLayout.BuffsPerRow && h >= 720) Assert.True(chat.Y < h / 3, $"{name} {s}: {chat} is not near the top");
                }
    }

    [Fact]
    public void DebugOverlaySitsBelowTheHotbarAndBuffRows()
    {
        foreach (var (w, h, name) in Viewports())
            foreach (int buffs in new[] { 0, 1, 11, 12, 22, 44 })
            {
                var s = new UiState(BuffCount: buffs, InfoLines: 6, DebugOverlay: true, BossBar: buffs > 11);
                var layout = UiLayout.Compute(w, h, s);
                if (!layout.TryGet(UiRegion.Debug, out var debug))
                {
                    Assert.True(buffs > UiLayout.BuffsPerRow, $"{name} {s}: no room for the F3 overlay");
                    continue;
                }
                Assert.True(debug.Y >= layout.Get(UiRegion.Hotbar).Bottom && debug.Y >= layout.Get(UiRegion.Buffs).Bottom,
                    $"{name} {s}: F3 overlay {debug} over the hotbar or buffs");
                Assert.True(debug.W >= UiLayout.DebugMinWidth && debug.H >= UiLayout.DebugMinHeight);
                if (w >= 1600 && h >= 900) Assert.Equal(UiLayout.DebugWidth, debug.W); // full width where there is room
            }
    }

    [Fact]
    public void SlotsShrinkOnlyWhenTheInventoryDoesNotFit()
    {
        foreach (var s in new[] { Inventory, Chest, Shop, Service })
        {
            Assert.Equal(1f, UiLayout.Compute(1920, 1080, s).SlotScale);
            Assert.Equal(1f, UiLayout.Compute(1280, 720, s).SlotScale);
        }
        var small = UiLayout.Compute(1024, 600, Chest);
        Assert.True(small.SlotScale < 1f);
        Assert.Contains(small.SlotScale, UiLayout.SlotScales);
        Assert.Equal(1f, UiLayout.Compute(1024, 600, Hud).SlotScale);
    }

    [Fact]
    public void SettingsButtonSitsInTheBottomRightCornerOrAboveTheEventBar()
    {
        foreach (var (w, h) in new[] { (1280f, 720f), (1920f, 1080f) })
        {
            var corner = UiLayout.Compute(w, h, Chest).Get(UiRegion.SettingsButton);
            Assert.Equal(w - UiLayout.Margin, corner.Right, 3);
            Assert.Equal(h - UiLayout.Margin, corner.Bottom, 3);
            var crowded = UiLayout.Compute(w, h, Chest with { EventBar = true });
            var ev = crowded.Get(UiRegion.EventBar);
            var settings = crowded.Get(UiRegion.SettingsButton);
            Assert.Equal(w - UiLayout.Margin, settings.Right, 3);
            Assert.True(settings.Bottom <= ev.Y, $"{w}x{h}: settings {settings}, event bar {ev}");
        }
        Assert.False(UiLayout.Compute(1920, 1080, Hud).Has(UiRegion.SettingsButton));
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 10, 0, 10, 10, false)] // touching edges
    [InlineData(0, 0, 10, 10, 9, 9, 10, 10, true)]
    [InlineData(0, 0, 10, 10, 2, 2, 0, 5, false)] // empty
    public void RectsIntersectOnlyWhenTheyShareArea(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh, bool hit)
    {
        Assert.Equal(hit, new UiRect(ax, ay, aw, ah).Intersects(new UiRect(bx, by, bw, bh)));
        Assert.Equal(hit, new UiRect(bx, by, bw, bh).Intersects(new UiRect(ax, ay, aw, ah)));
        Assert.True(new UiRect(2, 2, 4, 4).Inside(new UiRect(0, 0, 10, 10)));
        Assert.False(new UiRect(8, 2, 4, 4).Inside(new UiRect(0, 0, 10, 10)));
    }

    /// <summary>The regions a state must always show, at any size.</summary>
    private static IEnumerable<string> Expected(UiState s)
    {
        yield return UiRegion.ItemName;
        yield return UiRegion.LifeText;
        yield return UiRegion.Hearts;
        yield return UiRegion.Mana;
        yield return UiRegion.Chat;
        yield return UiRegion.Breath;
        yield return UiRegion.StatusLine;
        if (s.BuffCount > 0) yield return UiRegion.Buffs;
        if (s.InventoryOpen)
        {
            yield return UiRegion.Inventory;
            yield return UiRegion.CoinAmmo;
            yield return UiRegion.Trash;
            yield return UiRegion.SortButtons;
            yield return UiRegion.Crafting;
            yield return UiRegion.Equipment;
            yield return UiRegion.SettingsButton;
            if (s.Panel != UiPanel.None) yield return UiRegion.Panel;
            yield break;
        }
        yield return UiRegion.Hotbar;
        if (s.ShowMinimap) yield return UiRegion.Minimap;
        if (s.InfoLines > 0) yield return UiRegion.Info;
        if (s.BossBar) yield return UiRegion.BossBar;
        if (s.EventBar) yield return UiRegion.EventBar;
        if (s.NpcChat) yield return UiRegion.NpcChat;
        else yield return UiRegion.Crosshair;
        if (s.DebugOverlay && s.BuffCount <= UiLayout.BuffsPerRow && !s.NpcChat) yield return UiRegion.Debug;
    }
}
