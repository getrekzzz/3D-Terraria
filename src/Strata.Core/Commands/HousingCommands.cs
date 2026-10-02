using Strata.Core.Common;
using Strata.Core.Gameplay.Town;

namespace Strata.Core.Commands;

/// <summary>The housing menu through the console: query a room, assign a resident to it, or evict them.</summary>
public static class HousingCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "housing", Usage = "housing query [dx dy dz] | assign <id> [dx dy dz] | evict <id>",
            Help = "Housing queries and assignments (cells relative to the player's feet; default the cell above them).",
            Run = (ctx, a) =>
            {
                var town = ctx.World.Town;
                switch (a.OptStr(0))
                {
                    case "query":
                        return CommandResult.Success(town.QueryHousing(Cell(ctx, a, 1)).Message);
                    case "assign":
                        var who = TownCommands.Resident(ctx, a);
                        var result = town.AssignHome(who, Cell(ctx, a, 2), out var message);
                        return result is AssignResult.Assigned or AssignResult.Swapped or AssignResult.AlreadyHome
                            ? CommandResult.Success($"{result}: {message}")
                            : CommandResult.Fail($"{result}: {message}");
                    case "evict":
                        var evicted = TownCommands.Resident(ctx, a);
                        return town.Evict(evicted) ? CommandResult.Success($"{evicted.Title} was evicted.") : CommandResult.Fail($"{evicted.Title} has no home.");
                    default:
                        throw new CommandException("usage: housing query|assign|evict");
                }
            },
        });
    }

    private static Int3 Cell(CommandContext ctx, CommandArgs a, int at) =>
        ctx.Player.FeetCell + (a.Count >= at + 3 ? new Int3(a.Int(at, "dx"), a.Int(at + 1, "dy"), a.Int(at + 2, "dz")) : Int3.Up);
}
