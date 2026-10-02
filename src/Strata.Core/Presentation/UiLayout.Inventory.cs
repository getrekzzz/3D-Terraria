namespace Strata.Core.Presentation;

/// <summary>The inventory screen's placement rules.</summary>
public static partial class UiLayout
{
    /// <summary>
    /// The inventory screen at slot scale <paramref name="scale"/>: the 10×5 grid on the hotbar's origin, the coin and
    /// ammo columns right of it with the trash under the ammo and the sort buttons beside them, the chest, shop or
    /// service panel under the grid, the crafting column from the bottom-left up to the panel and the equipment column
    /// under the minimap on the right (the minimap hides while the inventory is open when the column does not fit under
    /// it). Chat moves right of the crafting column and the buffs right of the panel. False when a required region does
    /// not fit, so the caller retries with smaller slots.
    /// </summary>
    private static bool PlaceInventory(UiLayoutPass p, UiState s, float scale)
    {
        float h = p.Height;
        float slot = Slot * scale, small = SmallSlot * scale;
        var grid = new UiRect(Margin, HotbarY, 10 * slot + 9 * Gap, 5 * slot + 4 * Gap);
        p.Put(UiRegion.ItemName, new UiRect(Margin, 2, grid.W, TextLine));
        p.Put(UiRegion.Inventory, grid);
        var hearts = PlaceVitals(p);
        var coinAmmo = new UiRect(grid.Right + 2 * Gap, HotbarY, 2 * small + Gap, Caption + 4 * small + 3 * Gap);
        var trash = new UiRect(coinAmmo.Right - small, coinAmmo.Bottom + Gap, small, small);
        var sort = new UiRect(coinAmmo.Right + Gap, HotbarY + Caption, SortButton, 2 * SortButton + Gap);
        if (!PutInside(p, UiRegion.CoinAmmo, coinAmmo) || !PutInside(p, UiRegion.Trash, trash)
            || !PutInside(p, UiRegion.SortButtons, sort) || !PlaceEquipment(p, s, hearts, scale)) return false;

        float top = grid.Bottom;
        if (s.Panel != UiPanel.None)
        {
            var panel = s.Panel == UiPanel.Service
                ? new UiRect(Margin, grid.Bottom + 2 * Gap, ServiceWidth, Caption + slot + Gap + TextLine)
                : new UiRect(Margin, grid.Bottom + 2 * Gap, 10 * PanelSlot * scale + 11 * Gap + PanelButtonsWidth,
                    Caption + 4 * PanelSlot * scale + 3 * Gap);
            if (!PutInside(p, UiRegion.Panel, panel)) return false;
            top = panel.Bottom;
        }
        float craftW = CraftSelectedScale * slot + 2 * Gap + CraftDetailWidth;
        float craftMinH = MathF.Max(CraftSelectedScale * slot, CraftDetailHeight);
        var craft = new UiRect(Margin, top + 2 * Gap, craftW, h - Margin - top - 2 * Gap);
        if (!p.PutShrunk(UiRegion.Crafting, craft, true, false, craftW, craftMinH)) return false;
        craft = p.Get(UiRegion.Crafting);

        var chatMin = new UiRect(craft.Right + 2 * Gap, h - Margin - ChatMinLines * ChatLine, ChatMinWidth, ChatMinLines * ChatLine);
        if (!p.TryPut(UiRegion.Chat, chatMin)) return false;
        float clusterBottom = MathF.Max(trash.Bottom, sort.Bottom);
        float middleX = MathF.Max(coinAmmo.X, p.Has(UiRegion.Panel) ? p.Get(UiRegion.Panel).Right + 2 * Gap : 0);
        if (s.BuffCount > 0) PlaceInventoryBuffs(p, s.BuffCount, middleX, clusterBottom + 2 * Gap, craft, top);
        PlaceInventoryInfo(p, s);
        if (s.NpcChat && !TryNpcChatTop(p, sort.Right, hearts.X)) // else the nearest free spot to the top centre
            p.PutNear(UiRegion.NpcChat, new UiRect(p.Width / 2 - NpcChatWidth / 2, HotbarY, NpcChatWidth, NpcChatHeight));
        var crosshair = new UiRect(p.Width / 2 - CrosshairWidth / 2, h / 2 - CrosshairTop, CrosshairWidth, CrosshairHeight);
        PlaceCentre(p, crosshair, new UiRect(p.Width / 2 - BreathWidth / 2, crosshair.Y - Gap - BreathHeight, BreathWidth, BreathHeight));
        PlaceBars(p, s, chatMin);
        if (s.DebugOverlay)
        {
            float y = (p.Has(UiRegion.Buffs) ? p.Get(UiRegion.Buffs).Bottom : clusterBottom) + 2 * Gap;
            if (!p.PutShrunk(UiRegion.Debug, new UiRect(middleX, y, DebugWidth, MathF.Min(DebugHeight, h - Margin - y)), true, true, DebugMinWidth, DebugMinHeight))
                p.PutShrunk(UiRegion.Debug, new UiRect(chatMin.X, top + 2 * Gap, DebugWidth, MathF.Min(DebugHeight, h - Margin - top - 2 * Gap)), true, true,
                    DebugMinWidth, DebugMinHeight);
        }
        GrowChat(p, new UiRect(chatMin.X, h - Margin - ChatLines * ChatLine, ChatWidth, ChatLines * ChatLine), chatMin);
        return p.Has(UiRegion.Breath) && p.Has(UiRegion.StatusLine) && (s.BuffCount <= 0 || p.Has(UiRegion.Buffs));
    }

    /// <summary>Places <paramref name="r"/> when it fits inside the screen margins and overlaps nothing.</summary>
    private static bool PutInside(UiLayoutPass p, string region, UiRect r) =>
        r.Bottom <= p.Height - Margin + 0.01f && r.Right <= p.Width - Margin + 0.01f && p.TryPut(region, r);

    /// <summary>
    /// The equipment column under the minimap, right-aligned with the hearts; under the hearts with the minimap hidden
    /// when it does not fit there.
    /// </summary>
    private static bool PlaceEquipment(UiLayoutPass p, UiState s, UiRect hearts, float scale)
    {
        float w = EquipmentWidth(scale), h = EquipmentHeight(s.AccessoryRows, scale);
        if (s.ShowMinimap)
        {
            var mini = MinimapRect(hearts);
            if (PutInside(p, UiRegion.Minimap, mini))
            {
                if (PutInside(p, UiRegion.Equipment, new UiRect(hearts.Right - w, mini.Bottom + Gap, w, h))) return true;
                p.Remove(UiRegion.Minimap);
            }
            p.MinimapHidden = true;
        }
        return PutInside(p, UiRegion.Equipment, new UiRect(hearts.Right - w, hearts.Bottom + Gap, w, h));
    }

    /// <summary>
    /// The buff icons right of the panel under the coin, ammo and trash slots, as many per row as fit before the
    /// equipment column; right of the crafting column when that is full.
    /// </summary>
    private static void PlaceInventoryBuffs(UiLayoutPass p, int count, float x, float y, UiRect craft, float panelBottom)
    {
        float right = (p.Has(UiRegion.Equipment) ? p.Get(UiRegion.Equipment).X : p.Width - Margin) - Gap;
        if (TryBuffs(p, count, x, y, right)) return;
        TryBuffs(p, count, craft.Right + 2 * Gap, panelBottom + 2 * Gap, right);
    }

    private static bool TryBuffs(UiLayoutPass p, int count, float x, float y, float right)
    {
        int perRow = Math.Clamp((int)((right - x + BuffPitchX - BuffIcon) / BuffPitchX), 1, BuffsPerRow);
        var r = new UiRect(x, y, BuffRowWidth(Math.Min(Math.Min(count, MaxBuffs), perRow)), BuffRowsHeight(BuffRows(count, perRow)));
        return p.PutShrunk(UiRegion.Buffs, r, true, true, BuffIcon, BuffCellHeight);
    }

    /// <summary>The info panel left of the equipment column, or under it, cut short when crowded; left out when no room.</summary>
    private static void PlaceInventoryInfo(UiLayoutPass p, UiState s)
    {
        if (s.InfoLines <= 0 || !p.Has(UiRegion.Equipment)) return;
        var eq = p.Get(UiRegion.Equipment);
        float h = s.InfoLines * InfoLine;
        if (p.PutShrunk(UiRegion.Info, new UiRect(eq.X - 2 * Gap - InfoWidth, eq.Y, InfoWidth, h), false, true, InfoMinWidth, InfoLine))
            return;
        p.PutShrunk(UiRegion.Info, new UiRect(eq.Right - InfoWidth, eq.Bottom + Gap, InfoWidth, h), false, true, InfoMinWidth, InfoLine);
    }
}
