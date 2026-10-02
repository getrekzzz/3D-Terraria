using Godot;
using Strata.Core.Commands;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Bootstrap;
using Strata.Game.Settings;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>Commands for the interface: opening gameplay screens, the UI scale, and exporting the theme resource.</summary>
public static class UiCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "ui", Usage = "ui <inventory|map|talk <resident>|shop|hover <area> <index>|click <left|right> [down|up]|drag <dx> <dy>|minimap <in|out>|f3|sort|quickstack|tab <equipment|misc>|eye <accessory>|chest <loot|deposit|quickstack|restock|sort|rename|name <text>|cancel>|close|export-theme>",
            Help = "Opens or closes gameplay screens; hover shows a slot's tooltip (areas as invclick, plus hotbar); click and drag press on empty screen beside an open screen, through the GUI; minimap presses a zoom button when it would take a click; f3 toggles the performance overlay; sort and quickstack press the inventory's Sort Inventory and Quick Stack to Nearby Chests buttons; tab shows an equipment column page; eye presses an accessory row's visibility toggle; chest presses an open chest's link (rename opens the name field, name types a name and presses Enter, cancel presses Esc); export-theme rewrites assets/ui/strata_theme.tres from the code-built theme.",
            Run = (ctx, a) =>
            {
                string what = a.Str(0, "screen");
                if (what == "export-theme")
                {
                    var error = UiTheme.Export();
                    return error == Error.Ok ? CommandResult.Success($"Theme saved to {UiTheme.ThemePath}.") : CommandResult.Fail($"Could not save the theme: {error}.");
                }
                var screen = world.Inventory ?? throw new CommandException("no inventory screen in this mode");
                var dialog = world.TownDialog ?? throw new CommandException("no town dialog in this mode");
                switch (what)
                {
                    case "inventory": screen.SetOpen(true); return CommandResult.Success("Inventory open.");
                    case "map":
                        (world.WorldMap ?? throw new CommandException("no map in this mode")).SetOpen(true);
                        return CommandResult.Success("Map open.");
                    case "talk":
                        var id = a.Str(1, "resident");
                        var npc = ctx.World.Town.Find(id) ?? throw new CommandException($"'{id}' does not live in town");
                        if (!ctx.World.Town.BeginTalk(npc, ctx.Player)) return CommandResult.Fail($"{npc.Title} is too far away.");
                        dialog.Open(npc);
                        return CommandResult.Success($"Talking to {npc.Title}.");
                    case "shop":
                        if (!dialog.IsOpen) return CommandResult.Fail("Not talking to anyone.");
                        dialog.OpenShop();
                        return CommandResult.Success("Shop open.");
                    case "hover":
                        string area = a.Str(1, "area");
                        int index = a.Int(2, "index");
                        var view = area == "hotbar"
                            ? world.Hud?.HotbarSlot(index)
                            : screen.FindSlot(new SlotRef(InventoryCommands.Area(area), index));
                        if (view == null || !view.IsVisibleInTree()) return CommandResult.Fail($"No {area} slot {index} on screen (open the inventory first).");
                        view.ShowTooltip();
                        return CommandResult.Success($"Tooltip: {world.Tooltip?.LineCount ?? 0} lines.");
                    case "click": return PointerInput.Click(world, a.Str(1, "button"), a.OptStr(2));
                    case "drag": return PointerInput.Drag(world, a.Float(1, "dx"), a.Float(2, "dy"));
                    case "minimap":
                        var mini = world.Runtime.GetNodeOrNull<Minimap>("Minimap") ?? throw new CommandException("no minimap in this mode");
                        if (!mini.Press(a.Str(1, "in|out") == "in")) return CommandResult.Fail("The minimap's buttons take no clicks now (hidden, or the mouse is captured).");
                        return CommandResult.Success($"Minimap zoom {mini.Zoom:0.##}.");
                    case "f3":
                        var perf = world.Runtime.GetNodeOrNull<PerfOverlay>("PerfOverlay") ?? throw new CommandException("no performance overlay in this mode");
                        perf.Toggle();
                        return CommandResult.Success(perf.IsOpen ? "Performance overlay open." : "Performance overlay closed.");
                    case "sort":
                        screen.Sort();
                        return CommandResult.Success("Backpack sorted.");
                    case "quickstack":
                        int chests = screen.QuickStack();
                        return CommandResult.Success(chests > 0 ? $"Quick stacked into {chests} chest(s)." : "No nearby chest holds any of these items.");
                    case "tab":
                        string page = a.Str(1, "equipment|misc");
                        if (page is not ("equipment" or "misc")) throw new CommandException("the tab is equipment or misc");
                        screen.Equipment.ShowMisc(page == "misc");
                        return CommandResult.Success($"Showing the {page} page.");
                    case "eye":
                        int row = a.Int(1, "accessory");
                        if (!screen.Equipment.PressEye(row)) return CommandResult.Fail($"Accessory row {row} has no eye on screen (open the inventory on the equipment page).");
                        return CommandResult.Success($"Accessory {row} {(ctx.Player.State.Inventory.IsAccessoryHidden(row) ? "hidden" : "shown")}.");
                    case "chest": return ChestPanelCommand(screen.Chest, a.Str(1, "button"), a.Count > 2 ? a.Rest(2) : "");
                    case "close":
                        screen.SetOpen(false);
                        dialog.Close();
                        world.WorldMap?.SetOpen(false);
                        return CommandResult.Success("Closed.");
                    default: throw new CommandException("expected inventory, map, talk, shop, hover, click, drag, minimap, f3, sort, quickstack, tab, eye, chest, close or export-theme");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "uiscale", Usage = "uiscale <factor>",
            Help = "Sets the UI scale for this session (not saved); the window size may lower the effective scale (probe ui.scale).",
            Run = (_, a) =>
            {
                float factor = a.Float(0, "factor");
                if (factor is < 0.5f or > 3f) throw new CommandException("factor must be within 0.5..3");
                UserSettings.Current.UiScale = factor;
                DisplaySettings.ApplyUiScale(world.Runtime.GetTree().Root, factor);
                return CommandResult.Success(DisplaySettings.UiScaleClamped
                    ? $"UI scale {factor:0.##} (effective {DisplaySettings.EffectiveUiScale:0.##}: the window is too small)."
                    : $"UI scale {DisplaySettings.EffectiveUiScale:0.##}.");
            },
        });
    }

    /// <summary>The <c>ui chest</c> command: the open chest panel's links and its name field, as the player presses them.</summary>
    private static CommandResult ChestPanelCommand(ChestPanel chest, string button, string text)
    {
        if (chest.Container == null || !chest.IsVisibleInTree()) return CommandResult.Fail("No chest is shown (interact with one first).");
        ChestOp? op = button switch
        {
            "loot" => ChestOp.LootAll,
            "deposit" => ChestOp.DepositAll,
            "quickstack" => ChestOp.QuickStack,
            "restock" => ChestOp.Restock,
            "sort" => ChestOp.Sort,
            _ => null,
        };
        if (op is { } pressed) return CommandResult.Success($"{pressed}: {chest.Press(pressed)} items moved.");
        switch (button)
        {
            case "rename":
                chest.StartRename();
                return CommandResult.Success("Typing the chest's name.");
            case "name":
                return chest.SubmitRename(text) ? CommandResult.Success($"Named \"{chest.Title}\".") : CommandResult.Fail("The name field is not open (ui chest rename first).");
            case "cancel":
                return chest.CancelRename() ? CommandResult.Success("Renaming cancelled.") : CommandResult.Fail("The name field is not open.");
            default: throw new CommandException("expected loot, deposit, quickstack, restock, sort, rename, name or cancel");
        }
    }
}
