using Godot;
using Strata.Core.Commands;
using Strata.Core.Gameplay.Items;
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
            Name = "ui", Usage = "ui <inventory|map|talk <resident>|shop|hover <area> <index>|buff <cancel|hover> <id>|click <left|right> [down|up]|drag <dx> <dy>|minimap <in|out>|f3|sort|quickstack|chat <text>|history|close|export-theme>",
            Help = "Opens or closes gameplay screens; hover shows a slot's tooltip (areas as invclick, plus hotbar); buff cancel right-clicks a buff bar icon (a buff id, a minion's projectile id or minecart): buffs go, debuffs never, minions go home; buff hover shows its name and effect; click and drag press on empty screen beside an open screen, through the GUI; minimap presses a zoom button when it would take a click; f3 toggles the performance overlay; sort and quickstack press the inventory's Sort Inventory and Quick Stack to Nearby Chests buttons; chat posts a white line to the chat log and history opens or closes its history (the chat key); export-theme rewrites assets/ui/strata_theme.tres from the code-built theme.",
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
                    case "buff":
                        string action = a.Str(1, "cancel|hover");
                        if (action is not ("cancel" or "hover")) throw new CommandException("expected: ui buff <cancel|hover> <id>");
                        var bar = world.Hud?.Buffs ?? throw new CommandException("no HUD in this mode");
                        string buffId = a.Str(2, "id");
                        var icon = bar.Find(buffId);
                        if (icon == null) return CommandResult.Fail($"No '{buffId}' icon on the buff bar.");
                        if (action == "hover")
                        {
                            icon.ShowTooltip();
                            return CommandResult.Success($"Tooltip: {world.Tooltip?.LineCount ?? 0} lines.");
                        }
                        // A refusal (a debuff) is what the player would get, not a script error: the probes tell.
                        return CommandResult.Success(bar.Cancel(icon) ? $"Cancelled {buffId}." : $"{buffId} cannot be cancelled.");
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
                    case "chat":
                        var log = world.Hud?.Chat ?? throw new CommandException("no HUD in this mode");
                        log.Add(a.Count > 1 ? a.Rest(1) : throw new CommandException("missing <text>"), UiTheme.Text);
                        return CommandResult.Success($"Chat: {log.LinesShown} line(s) shown.");
                    case "history":
                        var chat = world.Hud?.Chat ?? throw new CommandException("no HUD in this mode");
                        chat.SetOpen(!chat.IsOpen);
                        return CommandResult.Success(chat.IsOpen ? "Chat history open." : "Chat history closed.");
                    case "close":
                        world.Hud?.Chat.SetOpen(false);
                        screen.SetOpen(false);
                        dialog.Close();
                        world.WorldMap?.SetOpen(false);
                        return CommandResult.Success("Closed.");
                    default: throw new CommandException("expected inventory, map, talk, shop, hover, buff, click, drag, minimap, f3, sort, quickstack, chat, history, close or export-theme");
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
}
