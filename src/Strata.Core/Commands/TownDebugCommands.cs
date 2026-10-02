namespace Strata.Core.Commands;

/// <summary>
/// Debug and smoke-test subcommands of <c>town</c> (slay; later bind, rescue, pose, visit), kept apart from
/// <see cref="TownCommands"/> so it stays small. Returns null for an unknown subcommand.
/// </summary>
internal static class TownDebugCommands
{
    public const string Usage = "slay <id> | services <id> | status <id>";

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
            case "services":
                var npc = TownCommands.Resident(ctx, a);
                var tabs = ctx.World.Commerce.Tabs(npc);
                return CommandResult.Success($"{npc.Title}: tabs [{string.Join(", ", tabs)}], services [{string.Join(", ", npc.Def.AllServices.Select(s => s.Type))}]");
            case "status":
                var dryad = TownCommands.Resident(ctx, a);
                if (ctx.World.Services.WorldStatusText(dryad) == null) town.Status.CompleteNow(); // the console waits for the sampler
                return ctx.World.Services.WorldStatusText(dryad) is { } text ? CommandResult.Success(text) : CommandResult.Fail($"{dryad.Title} offers no world status.");
            default:
                return null;
        }
    }
}
