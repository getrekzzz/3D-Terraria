namespace Strata.Core.Commands;

/// <summary>
/// Debug and smoke-test subcommands of <c>town</c> (slay; later bind, rescue, pose, visit), kept apart from
/// <see cref="TownCommands"/> so it stays small. Returns null for an unknown subcommand.
/// </summary>
internal static class TownDebugCommands
{
    public const string Usage = "slay <id> | services <id> | status <id> | bind <id> [dx dy dz] | rescue <id> | pose <id> <stand|sit|sleep|bound|lie> | defense on|off";

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
            case "bind":
                var def = ctx.World.Session.Content.TownNpcs.TryGet(a.Str(1, "id"), out var d) ? d : throw new CommandException($"unknown resident '{a.Str(1, "id")}'");
                var offset = a.Count >= 5 ? new Common.Int3(a.Int(2, "dx"), a.Int(3, "dy"), a.Int(4, "dz")) : new Common.Int3(3, 0, 0);
                var node = ctx.World.Navigator.NearestStandable(ctx.Player.FeetCell + offset, Gameplay.Navigation.NavAgent.Humanoid, 4) ?? throw new CommandException("no room to stand nearby");
                return CommandResult.Success($"{town.Bind(def, node).Title} waits to be freed.");
            case "rescue":
                var bound = TownCommands.Resident(ctx, a);
                return town.Rescue(bound, ctx.Player, out var said) ? CommandResult.Success($"{bound.Title}: {said}") : CommandResult.Fail(said.Length > 0 ? said : $"{bound.Title} is not bound.");
            case "defense":
                town.DefenseEnabled = a.Str(1, "on|off") != "off";
                return CommandResult.Success($"Residents {(town.DefenseEnabled ? "fight back" : "hold their fire")}.");
            case "pose":
                var posed = TownCommands.Resident(ctx, a);
                if (!Enum.TryParse<Gameplay.Town.ResidentPosture>(a.Str(2, "posture"), true, out var posture)) throw new CommandException($"unknown posture '{a.Str(2, "posture")}'");
                posed.Posture = posture;
                return CommandResult.Success($"{posed.Title} is now {posture.ToString().ToLowerInvariant()}.");
            default:
                return null;
        }
    }
}
