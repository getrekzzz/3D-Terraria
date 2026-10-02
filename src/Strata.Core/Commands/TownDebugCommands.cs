namespace Strata.Core.Commands;

/// <summary>
/// Debug and smoke-test subcommands of <c>town</c> (slay; later bind, rescue, pose, visit), kept apart from
/// <see cref="TownCommands"/> so it stays small. Returns null for an unknown subcommand.
/// </summary>
internal static class TownDebugCommands
{
    public const string Usage = "slay <id>";

    public static CommandResult? TryRun(CommandContext ctx, CommandArgs a)
    {
        var town = ctx.World.Town;
        switch (a.OptStr(0))
        {
            case "slay":
                var victim = TownCommands.Resident(ctx, a);
                if (!victim.Alive) return CommandResult.Fail($"{victim.Title} is already dead.");
                town.Slay(victim);
                return CommandResult.Success($"{victim.Title} was slain.");
            default:
                return null;
        }
    }
}
