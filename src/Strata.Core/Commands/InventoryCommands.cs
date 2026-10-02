using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Commands;

/// <summary>
/// Inventory slot clicks, throwing, chest buttons, sorting and quick stack: the same Core calls the inventory screen
/// makes, so smoke scripts and tests drive them.
/// </summary>
public static class InventoryCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "invclick", Usage = "invclick <area> <index> <left|right> [shift|ctrl|alt]…",
            Help = "Clicks an inventory slot (areas: backpack coins ammo trash armor accessories vanity_armor vanity_accessories dyes misc misc_dyes service container).",
            Run = (ctx, a) =>
            {
                var slot = new SlotRef(Area(a.Str(0, "area")), a.Int(1, "index"));
                bool right = a.Str(2, "left|right") switch
                {
                    "left" => false,
                    "right" => true,
                    _ => throw new CommandException("the button must be left or right"),
                };
                var mods = ClickMods.None;
                for (int i = 3; i < a.Count; i++)
                    mods |= a[i] switch
                    {
                        "shift" => ClickMods.Shift,
                        "ctrl" => ClickMods.Ctrl,
                        "alt" => ClickMods.Alt,
                        _ => throw new CommandException($"unknown modifier '{a[i]}'"),
                    };
                var outcome = ctx.World.ClickSlot(ctx.Player, slot, right, mods, Context(ctx, slot.Area == SlotArea.Container));
                return CommandResult.Success($"{outcome}; cursor {ctx.Player.State.Inventory.Cursor}");
            },
        });
        r.Register(new CommandDef
        {
            Name = "invthrow", Usage = "invthrow [one]", Help = "Throws the cursor stack (or one of it) where the player looks.",
            Run = (ctx, a) =>
            {
                bool one = a.OptStr(0) switch
                {
                    null => false,
                    "one" => true,
                    _ => throw new CommandException("the only option is 'one'"),
                };
                return CommandResult.Success(ctx.World.ThrowCursor(ctx.Player, one).ToString());
            },
        });
        r.Register(new CommandDef
        {
            Name = "chest", Usage = "chest [loot | deposit | quickstack | restock | sort | rename [name]]",
            Help = "Shows the chest opened last, or presses one of its buttons (rename with no name restores the default).",
            Run = (ctx, a) =>
            {
                var chest = Context(ctx, needChest: true).Chest!;
                if (a.OptStr(0) == "rename")
                {
                    ctx.World.RenameContainer(chest, a.Count > 1 ? a.Rest(1) : null);
                    return CommandResult.Success($"Named \"{chest.DisplayName}\".");
                }
                ChestOp? op = a.OptStr(0) switch
                {
                    null => null,
                    "loot" => ChestOp.LootAll,
                    "deposit" => ChestOp.DepositAll,
                    "quickstack" => ChestOp.QuickStack,
                    "restock" => ChestOp.Restock,
                    "sort" => ChestOp.Sort,
                    _ => throw new CommandException($"unknown chest button '{a[0]}'"),
                };
                if (op is { } button) return CommandResult.Success($"{button}: {ctx.World.ChestAction(ctx.Player, chest, button)} items moved.");
                var stacks = chest.Container!.Where(st => !st.IsEmpty).ToList();
                return CommandResult.Success($"{chest.DisplayName} at {chest.Anchor}: {stacks.Count}/{chest.Container!.Length} slots used" +
                    (stacks.Count > 0 ? ": " + string.Join(", ", stacks) : ""));
            },
        });
        r.Register(new CommandDef
        {
            Name = "invsort", Usage = "invsort", Help = "Sorts the backpack below the hotbar (favourites stay put).",
            Run = (ctx, _) =>
            {
                InventoryOps.Sort(ctx.Player.State.Inventory.Backpack, PlayerInventory.HotbarSize, PlayerInventory.BackpackSize);
                return CommandResult.Success("Sorted.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "quickstack", Usage = "quickstack [radius m]", Help = "Quick stack to nearby chests: items those chests already hold leave the backpack.",
            Run = (ctx, a) =>
            {
                var reached = ctx.World.QuickStackNearby(ctx.Player, a.Count > 0 ? a.Float(0, "radius") : GameWorld.QuickStackRadius);
                return CommandResult.Success(reached.Count == 0
                    ? "Nothing to stack."
                    : string.Join(", ", reached.Select(x => $"{x.Moved} into {x.Chest.DisplayName} at {x.Chest.Anchor}")));
            },
        });
        r.Register(new CommandDef
        {
            Name = "cursor", Usage = "cursor", Help = "Prints the stack held on the inventory cursor.",
            Run = (ctx, _) => CommandResult.Success(ctx.Player.State.Inventory.Cursor.ToString()),
        });
    }

    /// <summary>Parses an area name: the enum name in any case, with or without underscores ("vanity_armor"); "chest" is the container.</summary>
    public static SlotArea Area(string name)
    {
        if (name.Equals("chest", StringComparison.OrdinalIgnoreCase)) return SlotArea.Container;
        return Enum.TryParse<SlotArea>(name.Replace("_", ""), ignoreCase: true, out var area) && Enum.IsDefined(area)
            ? area
            : throw new CommandException($"unknown slot area '{name}'");
    }

    /// <summary>
    /// What the inventory screen would have open: the chest the player opened last while it still stands (required
    /// when <paramref name="needChest"/>), and the resident the player is talking to (their shop).
    /// </summary>
    internal static SlotContext Context(CommandContext ctx, bool needChest)
    {
        var chest = ctx.OpenContainer;
        if (chest != null && ctx.World.Session.World.TileObjects.Get(chest.Id) != chest) chest = ctx.OpenContainer = null;
        if (chest == null && needChest) throw new CommandException("no chest is open (interact with one first)");
        var shop = ctx.World.Town.Residents.FirstOrDefault(t => t.TalkingTo == ctx.Player);
        return chest != null ? SlotContext.Of(chest) : new SlotContext(Shop: shop);
    }
}
