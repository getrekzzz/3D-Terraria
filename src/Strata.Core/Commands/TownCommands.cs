using Strata.Core.Common;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Gameplay.Town;

namespace Strata.Core.Commands;

/// <summary>Housing and town commands.</summary>
public static class TownCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "house", Usage = "house [<dx> <dy> <dz>]",
            Help = "Checks whether the room around the player (or a cell relative to the feet) is a valid home.",
            Run = (ctx, a) =>
            {
                var cell = ctx.Player.FeetCell + (a.Count >= 3 ? new Int3(a.Int(0, "dx"), a.Int(1, "dy"), a.Int(2, "dz")) : new Int3(0, 1, 0));
                var room = ctx.World.Housing.Scan(cell);
                var owner = ctx.World.Town.Residents.FirstOrDefault(t => t.HomeRoom == room.Key && room.Valid);
                return room.Valid
                    ? CommandResult.Success($"This room is a valid home ({room.Interior.Count} cells, {room.FloorCells} floor){(owner != null ? $", home of {owner.Title}" : ", vacant")}.")
                    : CommandResult.Success($"This room is not a valid home: {string.Join(", ", room.Problems)}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "town", Usage = "town [check | summon <id> [dx dy dz] | dismiss <id> | talk <id> | buy <id> <item> [count] | sell <slot 1-50> | heal <id> | arrivals on|off]",
            Help = "Lists residents with their homes and prices, or manages the town.",
            Run = (ctx, a) =>
            {
                var town = ctx.World.Town;
                var commerce = ctx.World.Commerce;
                switch (a.OptStr(0))
                {
                    case null:
                        if (town.Residents.Count == 0) return CommandResult.Success("Nobody lives in town yet.");
                        return CommandResult.Success(string.Join("\n", town.Residents.Select(t =>
                            $"{t.Title}: {(t.Home is { } h ? $"home {h}" : "homeless")}, {t.Activity}, mood {commerce.Mood(t, out _):+0;-0;0}, prices ×{commerce.PriceFactor(t):0.00}")));
                    case "check":
                        var arrived = town.CheckArrivals();
                        return CommandResult.Success(arrived != null ? $"{arrived.Title} has arrived." : "Nobody moved in.");
                    case "summon":
                        var def = ctx.World.Session.Content.TownNpcs.TryGet(a.Str(1, "id"), out var d) ? d : throw new CommandException($"unknown resident '{a.Str(1, "id")}'");
                        var offset = a.Count >= 5 ? new Int3(a.Int(2, "dx"), a.Int(3, "dy"), a.Int(4, "dz")) : new Int3(3, 0, 0);
                        var node = ctx.World.Navigator.NearestStandable(ctx.Player.FeetCell + offset, NavAgent.Humanoid, 4) ?? throw new CommandException("no room to stand nearby");
                        return CommandResult.Success($"{town.Summon(def, node).Title} is here.");
                    case "dismiss":
                        return town.Dismiss(Resident(ctx, a)) ? CommandResult.Success("Dismissed.") : CommandResult.Fail("Not in town.");
                    case "talk":
                        var who = Resident(ctx, a);
                        if (!town.BeginTalk(who, ctx.Player)) return CommandResult.Fail($"{who.Title} is too far away.");
                        return CommandResult.Success($"{who.Title}: {commerce.Greet(who, ctx.Player)}");
                    case "buy":
                        var seller = Resident(ctx, a);
                        var item = ctx.World.Session.Content.Items.TryGet(a.Str(2, "item"), out var it) ? it : throw new CommandException($"unknown item '{a.Str(2, "item")}'");
                        var bought = commerce.Buy(seller, ctx.Player, item, Math.Clamp(a.OptInt(3, 1), 1, 999));
                        return bought == TradeResult.Ok ? CommandResult.Success($"Bought {item.DisplayName}.") : CommandResult.Fail(bought.ToString());
                    case "sell":
                        var sold = commerce.Sell(ctx.Player, Math.Clamp(a.Int(1, "slot"), 1, 50) - 1);
                        return sold == TradeResult.Ok ? CommandResult.Success("Sold.") : CommandResult.Fail(sold.ToString());
                    case "heal":
                        var healed = commerce.Heal(Resident(ctx, a), ctx.Player);
                        return healed == TradeResult.Ok ? CommandResult.Success("Healed.") : CommandResult.Fail(healed.ToString());
                    case "arrivals":
                        town.ArrivalsEnabled = a.Str(1, "on|off") == "on";
                        return CommandResult.Success($"Arrivals {(town.ArrivalsEnabled ? "on" : "off")}.");
                    default:
                        throw new CommandException("unknown town subcommand");
                }
            },
        });
    }

    private static TownNpc Resident(CommandContext ctx, CommandArgs a)
    {
        var id = a.Str(1, "id");
        return ctx.World.Town.Find(id) ?? throw new CommandException($"'{id}' does not live in town");
    }
}
