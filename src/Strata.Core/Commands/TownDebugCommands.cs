namespace Strata.Core.Commands;

/// <summary>
/// Debug and smoke-test subcommands of <c>town</c> (slay; later bind, rescue, pose, visit), kept apart from
/// <see cref="TownCommands"/> so it stays small. Returns null for an unknown subcommand.
/// </summary>
internal static class TownDebugCommands
{
    public const string Usage = "slay <id> | services <id> | status <id> | bind <id> [dx dy dz] | rescue <id> | visit <id> | leave <id> | pylons | pylon <n>";

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
            case "visit":
                var vdef = ctx.World.Session.Content.TownNpcs.TryGet(a.Str(1, "id"), out var vd) ? vd : throw new CommandException($"unknown visitor '{a.Str(1, "id")}'");
                if (vdef.Visit == null) return CommandResult.Fail($"{vdef.DisplayName} is not a visitor.");
                return town.ArriveVisitor(vdef) is { } visitor ? CommandResult.Success($"{visitor.Title} has arrived.") : CommandResult.Fail("no place to arrive at (needs a housed resident, or a site underground).");
            case "leave":
                var leaving = TownCommands.Resident(ctx, a);
                return town.SendAway(leaving) ? CommandResult.Success($"{leaving.Title} is leaving.") : CommandResult.Fail($"{leaving.Title} is not a visitor.");
            case "pylons":
                var all = town.Pylons.All();
                return CommandResult.Success(all.Count == 0 ? "No pylons placed." : string.Join("; ", all.Select((o, i) =>
                    $"{i + 1}: {o.DisplayName} at {o.Anchor} ({(town.Pylons.IsActive(o) ? "working" : "needs residents")})")));
            case "pylon":
                var pylons = town.Pylons.All();
                int n = a.Int(1, "n");
                if (n < 1 || n > pylons.Count) return CommandResult.Fail($"pylon must be 1..{pylons.Count}");
                var to = pylons[n - 1];
                var r = town.Pylons.Teleport(ctx.Player, to);
                return r == Gameplay.Town.PylonTeleport.Teleported ? CommandResult.Success($"Teleported to the {to.DisplayName}.") : CommandResult.Fail(r.ToString());
            default:
                return null;
        }
    }
}
