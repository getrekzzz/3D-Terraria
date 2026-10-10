namespace Strata.Core.Commands;

/// <summary>The <c>hint</c> command: list, reset, force or switch off the onboarding hints (X-10).</summary>
public static class HintCommands
{
    private const string Usage = "hint [list | reset | show <id> | due <id> | on | off]";

    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "hint", Usage = Usage,
            Help = "Onboarding hints: shown count, the list with their state, forget the shown ones, show one now, test one's conditions, or turn them on/off (saved as the interface setting).",
            Run = (ctx, a) =>
            {
                var hints = ctx.World.Hints;
                var shown = ctx.Player.State.Journal.HintsShown;
                var all = ctx.World.Session.Content.Hints;
                switch (a.OptStr(0))
                {
                    case null:
                        return CommandResult.Success($"hints {(hints.Enabled ? "on" : "off")}, {shown.Count}/{all.Count} shown, last: {hints.LastShown?.Id ?? "none"}");
                    case "list":
                        return CommandResult.Success(string.Join(", ", all.All.Select(h => (shown.Contains(h.Id) ? "+" : "-") + h.Id)));
                    case "reset":
                        hints.Reset(ctx.Player);
                        return CommandResult.Success("Hints will show again.");
                    case "on" or "off":
                        hints.Enabled = a.Str(0, "state") == "on";
                        return CommandResult.Success($"Hints {(hints.Enabled ? "on" : "off")}.");
                    case "show" or "due":
                        var id = a.Str(1, "id");
                        if (!all.TryGet(id, out var def)) throw new CommandException($"unknown hint '{id}'");
                        if (a.Str(0, "action") == "due") return CommandResult.Success(hints.IsDue(ctx.Player, def) ? "due" : "not due");
                        hints.Force(ctx.Player, def);
                        return CommandResult.Success(def.Text);
                    default:
                        throw new CommandException("usage: " + Usage);
                }
            },
        });
    }
}
