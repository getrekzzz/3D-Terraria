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
/// screen's layout (P2-15); on the hearts, mana stars and breath bubbles (P2-13); on the buff bar (P2-14); on the
/// chat log (P2-21); on the pickup popups (P2-22); on the boss bar and the invasion progress (P2-23); on the death
/// screen (P2-25); on the Smart Cursor (P2-29); on the equipment column (P2-16), the chest panel (P2-17), the
/// crafting column (P2-18), the NPC chat box (P2-26), the grid shop (P2-27) and the reforge slot (P2-28); and on the
/// pause menu and the inventory's Settings button (P2-30).
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
        // W4-POLISH: the same check on what each widget draws (its slots, captions and fields included), not just its region.
        ctx.AddProbe("layout.overlaps", () =>
        {
            var pairs = new System.Collections.Generic.List<string>();
            int count = LayoutWidgets.CountOverlaps(world.Runtime.GetTree(), pairs, content: true);
            foreach (var pair in pairs) log.Warn("ui", $"content overlap: {pair}");
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
        // P2-16: the equipment column. Accessory rows shown (unlocked, or locked and still holding an item), the
        // defence on the shield badge and the Misc page shown.
        ctx.AddProbe("ui.equip_rows", () => world.Inventory?.AccessoryRows ?? 0);
        ctx.AddProbe("ui.defense_badge", () => world.Inventory?.Equipment.Defense.Shown ?? -1);
        ctx.AddProbe("ui.misc_tab", () => world.Inventory?.Equipment.MiscShown == true ? 1 : 0);
        // P2-17: the chest panel. Its name field is open, the shown chest has a name of its own, and the chests put
        // away (each with the chest_close creak).
        ctx.AddProbe("ui.renaming", () => world.Inventory?.Chest.Renaming == true ? 1 : 0);
        ctx.AddProbe("ui.rename_fits", () => world.Inventory?.Chest.RenameFits == true ? 1 : 0); // W4-POLISH: on the caption row
        ctx.AddProbe("ui.chest_named", () => world.Inventory?.Chest.Container?.CustomName != null ? 1 : 0);
        ctx.AddProbe("ui.chests_closed", () => world.Inventory?.Chest.ClosedCount ?? 0);
        // P2-18: the crafting column. Recipe slots listed, and the times it was rebuilt since the inventory opened (an
        // idle screen rebuilds nothing, so a hovered recipe's tooltip stays).
        ctx.AddProbe("ui.recipes", () => world.Inventory?.Crafting.RecipesShown ?? 0);
        ctx.AddProbe("ui.recipe_rebuilds", () => world.Inventory?.Crafting.Rebuilds ?? 0);
        // P2-26: the NPC chat box is open in the top third of the screen and overlaps no other HUD widget.
        ctx.AddProbe("ui.npc_chat_top", () => world.TownDialog?.AtTopClear() == true ? 1 : 0);
        // P2-27: the grid shop. Cells showing a ware or a sold stack on its page, and the stacks waiting to be bought back.
        ctx.AddProbe("ui.shop_slots", () => world.Inventory?.Shop.SlotsShown ?? 0);
        ctx.AddProbe("ui.buyback", () => ctx.World.Commerce.Buyback(ctx.Player).Count);
        // P2-28: the reforge slot. The cost shown under it (copper, 0 while hidden), the hammer presses that reforged,
        // and whether the service slot holds an item (closing the inventory stows it).
        ctx.AddProbe("ui.service_cost", () => world.Inventory?.Service.CostShown ?? 0);
        ctx.AddProbe("ui.reforges", () => world.Inventory?.Service.Reforges ?? 0);
        ctx.AddProbe("ui.service_item", () => ctx.Player.State.Inventory.Service[0].IsEmpty ? 0 : 1);
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
        // P2-22: pickup popups on screen (repeat pickups of one item, and all coins, share one).
        ctx.AddProbe("ui.popups", () => world.Hud?.PopupsShown ?? 0);
        // P2-23: the boss bar (a boss group is up) and the invasion progress panel (an invasion runs or just ended).
        ctx.AddProbe("ui.bossbar", () => world.Hud?.BossBar.Active == true ? 1 : 0);
        ctx.AddProbe("ui.bossbar_fill", () => world.Hud?.BossBar.Fraction ?? 0);
        ctx.AddProbe("ui.eventbar", () => world.Hud?.EventBar.Active == true ? 1 : 0);
        ctx.AddProbe("ui.eventbar_percent", () => world.Hud?.EventBar.Percent ?? -1);
        // P2-25: the death screen is up (the player is dead; 0 again once they respawn).
        ctx.AddProbe("ui.death_dim", () => world.Hud?.Death.Shown == true ? 1 : 0);
        // The cause under the title ("smoke was slain.") is also the chat log's newest line.
        ctx.AddProbe("ui.death_cause", () => world.Hud is { } hud && hud.Death.Cause.Length > 0 && hud.Chat.LastLine == hud.Death.Cause ? 1 : 0);
        // P2-30: the pause menu is open (Esc, or the inventory's Settings button) and that button is on screen.
        ctx.AddProbe("ui.paused", () => UiCommands.Pause(world)?.IsOpen == true ? 1 : 0);
        ctx.AddProbe("ui.settings_button", () => world.Inventory?.SettingsButtonShown == true ? 1 : 0);
        // P2-29: Smart Cursor is on (X toggles it), and the target is its pick rather than the cell under the crosshair.
        ctx.AddProbe("ui.smart_cursor", () => Settings.UserSettings.Current.SmartCursor ? 1 : 0);
        ctx.AddProbe("ui.smart_target", () => world.Interaction?.SmartTarget == true ? 1 : 0);
    }
}
