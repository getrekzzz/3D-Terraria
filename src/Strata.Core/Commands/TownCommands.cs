using Strata.Core.Common;

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
                return room.Valid
                    ? CommandResult.Success($"This room is a valid home ({room.Interior.Count} cells, {room.FloorCells} floor).")
                    : CommandResult.Success($"This room is not a valid home: {string.Join(", ", room.Problems)}.");
            },
        });
    }
}
