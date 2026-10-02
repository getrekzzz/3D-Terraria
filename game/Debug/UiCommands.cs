using System.Linq;
using Godot;
using Strata.Core.Commands;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
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
            Name = "ui", Usage = "ui <inventory|map|talk <resident>|service <type>|shop [tab]|buy <item> [n]|sell <area> <slot>|buyback <i>|reforge [hit]|hover <area> <index>|buff <cancel|hover> <id>|click <left|right> [down|up]|drag <dx> <dy>|minimap <in|out>|f3|sort|quickstack|tab <equipment|misc>|eye <accessory>|chest <loot|deposit|quickstack|restock|sort|rename|name <text>|cancel>|craft <item> [n]|chat <text>|history|settings|close|export-theme|" + TownUiCommands.Usage + ">",
            Help = "Opens or closes gameplay screens; shop opens the shop of the resident being talked to (the chat box's Shop); buy clicks a ware's shop slot n times (onto the cursor); sell Shift-clicks a slot with the shop open; buyback Shift-clicks sold stack i (back into the backpack); reforge opens the reforge slot of the resident being talked to (the chat box's Reforge) and reforge hit presses its hammer; hover shows a slot's tooltip (areas as invclick, plus hotbar and shop); buff cancel right-clicks a buff bar icon (a buff id, a minion's projectile id or minecart): buffs go, debuffs never, minions go home; buff hover shows its name and effect; click and drag press on empty screen beside an open screen, through the GUI; minimap presses a zoom button when it would take a click; f3 toggles the performance overlay; sort and quickstack press the inventory's Sort Inventory and Quick Stack to Nearby Chests buttons; tab shows an equipment column page; eye presses an accessory row's visibility toggle; chest presses an open chest's link (rename opens the name field, name types a name and presses Enter, cancel presses Esc); craft clicks the crafting column's recipe for an item as the player does (selecting it, then crafting onto the cursor n times); chat posts a white line to the chat log and history opens or closes its history (the chat key); settings presses the inventory's bottom-right Settings button (the pause menu opens over it); close also closes the pause menu; export-theme rewrites assets/ui/strata_theme.tres from the code-built theme.",
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
                    case "service":
                        if (!dialog.IsOpen) return CommandResult.Fail("Not talking to anyone (ui talk <resident>).");
                        return dialog.PressService(a.Str(1, "type")) ? CommandResult.Success($"Pressed {a.Str(1, "type")}.") : CommandResult.Fail($"No '{a.Str(1, "type")}' service here.");
                    case "shop":
                        if (dialog.IsOpen && a.Count > 1) dialog.OpenShopTab(a.Str(1, "tab"));
                        else if (dialog.IsOpen) dialog.OpenShop();
                        else if (ctx.World.Town.Residents.FirstOrDefault(t => t.TalkingTo == ctx.Player) is { } seller) screen.OpenShop(seller);
                        else return CommandResult.Fail("Not talking to anyone.");
                        return screen.Panel == UiPanel.Shop
                            ? CommandResult.Success($"Shop open: {screen.Shop.SlotsShown} slots.")
                            : CommandResult.Fail("This resident sells nothing.");
                    case "buy" or "sell" or "buyback":
                        return ShopCommand(screen, ctx, what, a);
                    case "reforge":
                        return ReforgeCommand(screen, dialog, ctx, a.OptStr(1));
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
                    case "craft":
                        string item = a.Str(1, "item");
                        int times = a.OptInt(2, 1);
                        if (!screen.Crafting.IsVisibleInTree()) return CommandResult.Fail("The crafting column is not on screen (open the inventory first).");
                        int made = screen.Crafting.Craft(item, times);
                        if (made < 0) return CommandResult.Fail($"No recipe for '{item}' on the crafting column.");
                        var cursor = ctx.Player.State.Inventory.Cursor;
                        return made > 0 ? CommandResult.Success($"Crafted {made} time(s); holding {cursor}.") : CommandResult.Fail($"Could not craft '{item}'; holding {cursor}.");
                    case "chat":
                        var log = world.Hud?.Chat ?? throw new CommandException("no HUD in this mode");
                        log.Add(a.Count > 1 ? a.Rest(1) : throw new CommandException("missing <text>"), UiTheme.Text);
                        return CommandResult.Success($"Chat: {log.LinesShown} line(s) shown.");
                    case "history":
                        var chat = world.Hud?.Chat ?? throw new CommandException("no HUD in this mode");
                        chat.SetOpen(!chat.IsOpen);
                        return CommandResult.Success(chat.IsOpen ? "Chat history open." : "Chat history closed.");
                    case "settings":
                        if (!screen.PressSettings()) return CommandResult.Fail("The inventory's Settings button is not on screen (open the inventory first).");
                        return CommandResult.Success("Pressed Settings.");
                    case "close":
                        if (Pause(world) is { IsOpen: true } pause) pause.SetOpen(false);
                        world.Hud?.Chat.SetOpen(false);
                        screen.SetOpen(false);
                        dialog.Close();
                        world.WorldMap?.SetOpen(false);
                        world.TownPanels?.CloseAll();
                        return CommandResult.Success("Closed.");
                    default: return TownUiCommands.TryRun(ctx, a, world) ?? throw new CommandException("expected inventory, map, talk, shop, buy, sell, buyback, reforge, hover, buff, click, drag, minimap, f3, sort, quickstack, tab, eye, chest, craft, chat, history, settings, close, export-theme or " + TownUiCommands.Usage);
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

    /// <summary>The <c>ui buy</c>, <c>ui sell</c> and <c>ui buyback</c> commands: clicks on the open shop as the player makes them.</summary>
    private static CommandResult ShopCommand(InventoryScreen screen, CommandContext ctx, string what, CommandArgs a)
    {
        var shop = screen.Shop;
        if (shop.Npc == null || !shop.IsVisibleInTree()) return CommandResult.Fail("No shop is open (ui talk <resident>, then ui shop).");
        var inv = ctx.Player.State.Inventory;
        switch (what)
        {
            case "buy":
                string item = a.Str(1, "item");
                int bought = shop.Buy(item, a.OptInt(2, 1));
                if (bought < 0) return CommandResult.Fail($"'{item}' is not for sale here.");
                return bought > 0 ? CommandResult.Success($"Bought {bought}; holding {inv.Cursor}.") : CommandResult.Fail($"Could not buy '{item}'; holding {inv.Cursor}.");
            case "sell":
                var slot = new SlotRef(InventoryCommands.Area(a.Str(1, "area")), a.Int(2, "slot"));
                return screen.PressSlot(slot, false, ClickMods.Shift) == ClickOutcome.Sold
                    ? CommandResult.Success($"Sold; {ctx.World.Commerce.Buyback(ctx.Player).Count} stack(s) to buy back.")
                    : CommandResult.Fail("Not sold (empty, a favourite, coins, or something held on the cursor).");
            default:
                var result = shop.BuyBack(a.Int(1, "index"));
                return result == Strata.Core.Gameplay.Town.TradeResult.Ok
                    ? CommandResult.Success("Bought back.")
                    : CommandResult.Fail(result?.ToString() ?? "No such sold stack, or something is held on the cursor.");
        }
    }

    /// <summary>
    /// The <c>ui reforge</c> command: opens the reforge slot as the chat box's Reforge does (or for the resident still
    /// being talked to), and <c>ui reforge hit</c> presses the panel's hammer.
    /// </summary>
    private static CommandResult ReforgeCommand(InventoryScreen screen, TownDialog dialog, CommandContext ctx, string? button)
    {
        var inv = ctx.Player.State.Inventory;
        var panel = screen.Service;
        if (button == "hit")
        {
            if (panel.Npc == null || !panel.IsVisibleInTree()) return CommandResult.Fail("No reforge slot is open (ui talk <resident>, then ui reforge).");
            var result = panel.Hit();
            return result == Strata.Core.Gameplay.Town.TradeResult.Ok
                ? CommandResult.Success($"Reforged: {inv.Service[0]}; the next one costs {Coins.Format(panel.CostShown)}.")
                : CommandResult.Fail($"Not reforged ({result}): {(inv.Service[0].IsEmpty ? "the slot is empty" : inv.Service[0].ToString())}.");
        }
        if (button != null) throw new CommandException("expected ui reforge or ui reforge hit");
        if (dialog.IsOpen) dialog.OpenService(ServiceMode.Reforge);
        else if (ctx.World.Town.Residents.FirstOrDefault(t => t.TalkingTo == ctx.Player) is { } npc
                 && npc.Def.FindService(Strata.Core.Content.Specs.TownServiceSpecs.Reforge) != null)
            screen.OpenService(npc, ServiceMode.Reforge);
        else return CommandResult.Fail("Not talking to anyone who reforges.");
        return screen.Panel == UiPanel.Service
            ? CommandResult.Success($"Reforge slot open{(inv.Service[0].IsEmpty ? "" : $": {inv.Service[0]}, cost {Coins.Format(panel.CostShown)}")}.")
            : CommandResult.Fail("This resident does not reforge.");
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

    /// <summary>The world's pause menu (none in a free-camera world).</summary>
    public static PauseMenu? Pause(LaunchedWorld world) => world.Runtime.GetParent()?.GetNodeOrNull<PauseMenu>("PauseMenu");
}
