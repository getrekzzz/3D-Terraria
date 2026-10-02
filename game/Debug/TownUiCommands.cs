using Strata.Core.Commands;
using Strata.Core.Content.Specs;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary><c>ui</c> subcommands for the town's own panels: the Stylist's haircut (P5-21) and the housing menu (P5-29).</summary>
public static class TownUiCommands
{
    public const string Usage = "haircut <resident>";

    /// <summary>Runs a town panel subcommand, or returns null for one it does not know.</summary>
    public static CommandResult? TryRun(CommandContext ctx, CommandArgs a, LaunchedWorld world)
    {
        var panels = world.TownPanels ?? throw new CommandException("no town panels in this mode");
        switch (a.Str(0, "screen"))
        {
            case "haircut":
                var id = a.Str(1, "resident");
                var npc = ctx.World.Town.Find(id) ?? throw new CommandException($"'{id}' does not live in town");
                if (npc.Def.FindService(TownServiceSpecs.Haircut) == null) return CommandResult.Fail($"{npc.Title} does not cut hair.");
                panels.Haircut.Open(npc);
                return CommandResult.Success($"{npc.Title}'s haircut open.");
            default:
                return null;
        }
    }
}
