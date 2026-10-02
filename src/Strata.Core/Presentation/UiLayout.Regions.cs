namespace Strata.Core.Presentation;

/// <summary>The placement rules shared by the HUD and the inventory screen, and the HUD's own.</summary>
public static partial class UiLayout
{
    /// <summary>
    /// The HUD with the inventory closed. Top-left: the selected item's name, the hotbar and the buff rows (11 per row).
    /// Top-right: the mana column at the far right, the hearts left of it with the life text above, the minimap under
    /// the hearts and the info panel under the minimap. Centre: the crosshair with the breath bubbles above it and the
    /// status line under it. Bottom: chat on the left, the boss bar in the centre and the event bar on the right. The NPC
    /// chat box sits at the top centre between the hotbar and the hearts, or below the buff rows when that gap is
    /// narrower than <see cref="NpcChatMinWidth"/>. The F3 overlay takes the left column below the hotbar and buffs.
    /// </summary>
    private static void PlaceHud(UiLayoutPass p, UiState s)
    {
        float w = p.Width, h = p.Height;
        p.Put(UiRegion.ItemName, new UiRect(Margin, 2, HotbarWidth, TextLine));
        var hotbar = new UiRect(Margin, HotbarY, HotbarWidth, Slot);
        p.Put(UiRegion.Hotbar, hotbar);
        var hearts = PlaceVitals(p);
        if (s.ShowMinimap) p.Put(UiRegion.Minimap, MinimapRect(hearts));
        float topLeftBottom = hotbar.Bottom;
        int rows = BuffRows(s.BuffCount, BuffsPerRow);
        if (rows > 0)
        {
            var buffs = new UiRect(Margin, hotbar.Bottom + Gap,
                BuffRowWidth(Math.Min(Math.Min(s.BuffCount, MaxBuffs), BuffsPerRow)), BuffRowsHeight(rows));
            p.Put(UiRegion.Buffs, buffs);
            topLeftBottom = buffs.Bottom;
        }
        var chatMin = new UiRect(Margin, h - Margin - ChatMinLines * ChatLine, ChatMinWidth, ChatMinLines * ChatLine);
        p.Put(UiRegion.Chat, chatMin); // reserved so the bars leave chat room; grown last

        var crosshair = new UiRect(w / 2 - CrosshairWidth / 2, h / 2 - CrosshairTop, CrosshairWidth, CrosshairHeight);
        var breath = new UiRect(w / 2 - BreathWidth / 2, crosshair.Y - Gap - BreathHeight, BreathWidth, BreathHeight);
        if (s.NpcChat) PlaceNpcChat(p, hotbar.Right, hearts.X, topLeftBottom, breath.Y);
        p.TryPut(UiRegion.Crosshair, crosshair); // only an NPC chat box dropped over the screen centre displaces it
        PlaceCentre(p, crosshair, breath);
        PlaceBars(p, s, chatMin);
        PlaceInfo(p, s, hearts);
        if (s.DebugOverlay) PlaceDebug(p, topLeftBottom, chatMin.Y);
        GrowChat(p, new UiRect(Margin, h - Margin - ChatLines * ChatLine, ChatWidth, ChatLines * ChatLine), chatMin);
    }

    /// <summary>The mana column, the hearts and the life text; returns the hearts.</summary>
    private static UiRect PlaceVitals(UiLayoutPass p)
    {
        var mana = new UiRect(p.Width - Margin - ManaWidth, 2, ManaWidth, ManaHeight);
        var hearts = new UiRect(mana.X - 2 * Gap - HeartsWidth, HotbarY, HeartsWidth, HeartsHeight);
        p.Put(UiRegion.Mana, mana);
        p.Put(UiRegion.Hearts, hearts);
        p.Put(UiRegion.LifeText, new UiRect(hearts.X, 2, HeartsWidth, TextLine));
        return hearts;
    }

    /// <summary>The minimap under the hearts, right-aligned with them.</summary>
    private static UiRect MinimapRect(UiRect hearts) =>
        new(hearts.Right - MinimapSize, hearts.Bottom + Gap, MinimapSize, MinimapSize);

    /// <summary>
    /// The NPC chat box: at the top between the hotbar and the hearts, at least <see cref="NpcChatClearance"/> from
    /// each; otherwise below the buff rows, centred and clear of the right column, cut short to stay above the
    /// crosshair when it can still hold its content, else over the screen centre (the crosshair hint then yields).
    /// </summary>
    private static void PlaceNpcChat(UiLayoutPass p, float leftEdge, float rightEdge, float topLeftBottom, float centreTop)
    {
        if (TryNpcChatTop(p, leftEdge, rightEdge)) return;
        float w = MathF.Min(NpcChatWidth, p.Width - 2 * Margin);
        var r = new UiRect(Math.Clamp(p.Width / 2 - w / 2, Margin, p.Width - Margin - w), topLeftBottom + Gap, w, NpcChatHeight);
        for (int i = 0; i < 4 && p.Blocker(r) is { } b && b.X > r.X; i++)
            r = r with { X = MathF.Max(Margin, b.X - Gap - w) }; // left of the minimap and the info panel
        float above = centreTop - Gap - r.Y;
        if (above >= NpcChatMinHeight && p.TryPut(UiRegion.NpcChat, r with { H = MathF.Min(NpcChatHeight, above) })) return;
        if (!p.TryPut(UiRegion.NpcChat, r)) p.PutNear(UiRegion.NpcChat, r);
    }

    /// <summary>The NPC chat box in the top gap between <paramref name="leftEdge"/> and <paramref name="rightEdge"/>.</summary>
    private static bool TryNpcChatTop(UiLayoutPass p, float leftEdge, float rightEdge)
    {
        float x0 = leftEdge + NpcChatClearance, x1 = rightEdge - NpcChatClearance;
        if (x1 - x0 < NpcChatMinWidth) return false;
        float width = MathF.Min(NpcChatWidth, x1 - x0);
        float x = Math.Clamp(p.Width / 2 - width / 2, x0, x1 - width);
        return p.TryPut(UiRegion.NpcChat, new UiRect(x, HotbarY, width, NpcChatHeight));
    }

    /// <summary>The breath bubbles above the crosshair and the status line under it, each moved to free space if needed.</summary>
    private static void PlaceCentre(UiLayoutPass p, UiRect crosshair, UiRect breath)
    {
        p.PutNear(UiRegion.Breath, breath);
        p.PutNear(UiRegion.StatusLine,
            new UiRect(p.Width / 2 - StatusWidth / 2, crosshair.Bottom + Gap, StatusWidth, StatusHeight));
    }

    /// <summary>
    /// The event bar bottom-right and the boss bars bottom-centre, stacked upward one per boss group (as many as fit,
    /// down to one). When the bottom row is too narrow for the reserved chat, the boss bars and the event bar, the boss
    /// bars narrow to <see cref="BossBarMinWidth"/> between them, then stack above the event bar; failing all that, both
    /// move to the nearest free spot.
    /// </summary>
    private static void PlaceBars(UiLayoutPass p, UiState s, UiRect chatMin)
    {
        float w = p.Width, h = p.Height;
        float right = w - Margin;
        if (s.EventBar)
        {
            var ev = new UiRect(w - Margin - EventBarWidth, h - Margin - EventBarHeight, EventBarWidth, EventBarHeight);
            if (!p.TryPut(UiRegion.EventBar, ev)) p.PutNear(UiRegion.EventBar, ev);
            if (p.Has(UiRegion.EventBar) && p.Get(UiRegion.EventBar).Bottom > h - Margin - BossBarHeight - Gap)
                right = p.Get(UiRegion.EventBar).X - Gap;
        }
        if (!s.BossBar) return;
        for (int n = Math.Clamp(s.BossBars, 1, BossBarModel.MaxBars); n >= 1; n--)
            if (TryBossBars(p, BossBarsHeight(n), right, chatMin)) return;
        p.PutNear(UiRegion.BossBar, new UiRect(w / 2 - BossBarWidth / 2, h - Margin - BossBarHeight, BossBarWidth, BossBarHeight));
    }

    /// <summary>The boss bars, <paramref name="height"/> tall: centred at the bottom, squeezed, or above the event bar.</summary>
    private static bool TryBossBars(UiLayoutPass p, float height, float right, UiRect chatMin)
    {
        float w = p.Width, y = p.Height - Margin - height;
        var centre = new UiRect(w / 2 - BossBarWidth / 2, y, BossBarWidth, height);
        float left = chatMin.Right + Gap;
        float between = MathF.Min(BossBarWidth, right - left);
        var squeezed = new UiRect(MathF.Max(left, MathF.Min(centre.X, right - between)), y, between, height);
        if (p.TryPut(UiRegion.BossBar, centre)) return true;
        if (between >= BossBarMinWidth && p.TryPut(UiRegion.BossBar, squeezed)) return true;
        if (!p.Has(UiRegion.EventBar)) return false;
        var ev = p.Get(UiRegion.EventBar);
        return p.TryPut(UiRegion.BossBar, new UiRect(w - Margin - BossBarWidth, ev.Y - Gap - height, BossBarWidth, height));
    }

    /// <summary>The info panel under the minimap (or the hearts), cut short when something sits below it.</summary>
    private static void PlaceInfo(UiLayoutPass p, UiState s, UiRect hearts)
    {
        if (s.InfoLines <= 0) return;
        float top = (p.Has(UiRegion.Minimap) ? p.Get(UiRegion.Minimap).Bottom : hearts.Bottom) + Gap;
        var r = new UiRect(hearts.Right - InfoWidth, top, InfoWidth, s.InfoLines * InfoLine);
        p.PutShrunk(UiRegion.Info, r, false, true, InfoWidth, InfoLine);
    }

    /// <summary>
    /// The F3 overlay in the left column below the hotbar and buff rows (and the NPC chat box when it dropped there),
    /// down to the reserved chat lines, narrowed to stay clear of the screen centre.
    /// </summary>
    private static void PlaceDebug(UiLayoutPass p, float topLeftBottom, float chatTop)
    {
        float top = topLeftBottom + Gap;
        var r = new UiRect(Margin, top, DebugWidth, MathF.Min(DebugHeight, chatTop - Gap - top));
        if (p.PutShrunk(UiRegion.Debug, r, true, true, DebugMinWidth, DebugMinHeight)) return;
        if (p.Has(UiRegion.NpcChat) && p.Get(UiRegion.NpcChat).Bottom + Gap > top)
        {
            top = p.Get(UiRegion.NpcChat).Bottom + Gap;
            p.PutShrunk(UiRegion.Debug, new UiRect(Margin, top, DebugWidth, MathF.Min(DebugHeight, chatTop - Gap - top)), true, true,
                DebugMinWidth, DebugMinHeight);
        }
    }

    /// <summary>The chat log grown from its reserved minimum up to <paramref name="full"/>, anchored bottom-left.</summary>
    private static void GrowChat(UiLayoutPass p, UiRect full, UiRect reserved)
    {
        p.Remove(UiRegion.Chat);
        if (!p.PutShrunk(UiRegion.Chat, full, true, false, ChatMinWidth, ChatMinLines * ChatLine))
            p.Put(UiRegion.Chat, reserved);
    }
}
