using Godot;
using Strata.Core.Commands;
using Strata.Core.Diagnostics;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the global UI look (P2-01): the shipped font is ThemeDB's fallback, an unthemed panel resolves to
/// Terraria's blue, the exported theme file matches the code-built theme, and the effective UI scale; and on the
/// generated pixel sprites and buff/event icons the HUD preloads (P2-02, P2-03); on the enemy mini health bars; on
/// the floating combat numbers (P2-19); on the slots, the following tooltip and the held stack (P2-10); on the
/// overlay input gate and the empty-screen click catcher (P2-11); on the HUD layout (P2-12); on the inventory
/// screen's layout (P2-15); on the hearts, mana stars and breath bubbles (P2-13); on the buff bar (P2-14); and on the
/// chat log (P2-21).
/// </summary>
[ProbeSource]
public static class UiProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddProbe("ui.font", () => ThemeDB.FallbackFont?.ResourcePath == UiFonts.BoldPath ? 1 : 0);
        ctx.AddProbe("ui.theme", () =>
        {
            // A bare control with no theme of its own sees only the global themes, as every CanvasLayer's UI does.
            var probe = new PanelContainer();
            bool blue = probe.GetThemeStylebox("panel") is StyleBoxFlat flat && flat.BgColor.IsEqualApprox(UiTheme.PanelBlue);
            probe.Free();
            return blue ? 1 : 0;
        });
        ctx.AddProbe("ui.theme_synced", () =>
        {
            if (UiTheme.ExportInSync(out var difference)) return 1;
            log.Warn("ui", $"{UiTheme.ThemePath} is stale ({difference}); run: godot --headless --path . -- --export-theme");
            return 0;
        });
        ctx.AddProbe("ui.scale", () => world.Runtime.GetTree().Root.ContentScaleFactor);
        ctx.AddProbe("ui.warmup_ms", () => UiTheme.WarmupMs);
        ctx.AddProbe("ui.sprites", () => UiSprites.LoadedCount);
        ctx.AddProbe("ui.sprites_missing", () => UiSprites.MissingCount);
        ctx.AddProbe("ui.status_icons_missing", () => StatusIcons.MissingCount);
        ctx.AddProbe("hud.buff_icons", () => world.Hud?.BuffIconsShown ?? 0);
        Entities.NpcRenderer? creatures = null;
        ctx.AddProbe("fx.health_bars", () =>
        {
            creatures ??= world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures");
            return creatures?.HealthBarsShown ?? 0;
        });
        Entities.CombatEffects? effects = null;
        Entities.CombatEffects? Effects() => effects ??= world.Runtime.GetNodeOrNull<Entities.CombatEffects>("CombatEffects");
        ctx.AddProbe("fx.numbers", () => Effects()?.NumbersShown ?? 0);
        ctx.AddProbe("fx.heal_numbers", () => Effects()?.HealNumbersShown ?? 0);
        ctx.AddProbe("fx.number_labels", () => Effects()?.NumberLabels ?? 0);
        ctx.AddProbe("ui.tooltip_lines", () => world.Tooltip?.LineCount ?? 0);
        ctx.AddProbe("ui.cursor_count", () => world.CursorItem?.ShownCount ?? 0);
        ctx.AddProbe("ui.slot_views", () => ItemSlotView.ActiveViews);
        // P2-11: the inventory is an overlay (the player keeps moving); presses that landed on empty screen; the view's yaw.
        ctx.AddProbe("ui.inventory", () => world.Inventory?.IsOpen == true ? 1 : 0);
        ctx.AddProbe("ui.overlay", () => InputGate.OverlayOpen ? 1 : 0);
        ctx.AddProbe("ui.modal", () => InputGate.GameplayBlocked ? 1 : 0);
        ctx.AddProbe("ui.world_clicks", () => world.Runtime.GetNodeOrNull<WorldClickCatcher>(WorldClickCatcher.NodePath)?.Presses ?? 0);
        ctx.AddProbe("camera.yaw", () => world.Player is { } p ? Mathf.RadToDeg(p.Rig.Yaw) : 0);
        // P2-12: the HUD on the layout model. Overlapping widgets (each pair is logged), the line above the hotbar, a
        // message on the status line, the logical viewport the layout was computed for and the minimap's zoom.
        ctx.AddProbe("ui.overlaps", () =>
        {
            var pairs = new System.Collections.Generic.List<string>();
            int count = LayoutWidgets.CountOverlaps(world.Runtime.GetTree(), pairs);
            foreach (var pair in pairs) log.Warn("ui", $"overlap: {pair}");
            return count;
        });
        ctx.AddProbe("hud.item_name", () => world.Hud?.ItemName.Length > 0 ? 1 : 0);
        ctx.AddProbe("hud.status", () => world.Hud?.Status.Length > 0 ? 1 : 0);
        ctx.AddProbe("ui.layout_width", () => world.Hud?.Layout?.Width ?? 0);
        ctx.AddProbe("ui.minimap_zoom", () => world.Runtime.GetNodeOrNull<Minimap>("Minimap")?.Zoom ?? 0);
        // P2-15: the inventory screen on the layout. Its grid sits on the hotbar's origin; the panel under it (0 none,
        // 1 chest, 2 shop, 3 service); the slot scale the layout chose.
        ctx.AddProbe("ui.grid_on_hotbar", () => world.Inventory?.GridOnHotbar == true ? 1 : 0);
        ctx.AddProbe("ui.panel", () => (int)(world.Inventory?.Panel ?? Strata.Core.Presentation.UiPanel.None));
        ctx.AddProbe("ui.slot_scale", () => world.Inventory?.SlotScale ?? 0);
        // P2-13: Terraria's hearts (20 life each, golden past 400), mana stars and breath bubbles (0 at full breath).
        ctx.AddProbe("hud.hearts", () => world.Hud?.HeartsShown ?? 0);
        ctx.AddProbe("hud.golden_hearts", () => world.Hud?.GoldenHeartsShown ?? 0);
        ctx.AddProbe("hud.stars", () => world.Hud?.StarsShown ?? 0);
        ctx.AddProbe("hud.bubbles", () => world.Hud?.BubblesShown ?? 0);
        // P2-14: icons on the buff bar (buffs, minion kinds, the minecart).
        ctx.AddProbe("hud.buffs", () => world.Hud?.Buffs.Count ?? 0);
        // P2-21: chat log lines on show (not yet faded, or all kept ones in the history view) and the history view.
        ctx.AddProbe("ui.chat_lines", () => world.Hud?.Chat.LinesShown ?? 0);
        ctx.AddProbe("ui.chat_open", () => world.Hud?.Chat.IsOpen == true ? 1 : 0);
    }
}
