namespace Strata.Core.Commands;

/// <summary><c>season</c>: shows the seasonal event or forces one for this session (probes in <see cref="SeasonProbes"/>).</summary>
public static class SeasonCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "season", Usage = "season [halloween | christmas | off | auto]",
            Help = "Shows the seasonal event, forces one (or none with 'off') for this session, or follows the date again ('auto').",
            Run = (ctx, a) =>
            {
                var s = ctx.World.Session.Seasons;
                string? arg = a.OptStr(0);
                if (arg == null)
                    return CommandResult.Success($"Season: {(s.Current.Length > 0 ? s.Current : "none")}{(s.Override != null ? " (forced)" : " (by date)")}.");
                if (arg == "auto") s.Override = null;
                else if (arg is "off" or "none") s.Override = "";
                else if (s.Config.IndexOf(arg) > 0) s.Override = arg;
                else throw new CommandException($"unknown season '{arg}' (halloween, christmas, off, auto)");
                return CommandResult.Success($"Season: {(s.Current.Length > 0 ? s.Current : "none")}.");
            },
        });
    }
}

/// <summary><c>season.id</c>: 0 none, 1 Halloween, 2 Christmas (the order of data/config/seasons.json).</summary>
[ProbeSource]
internal static class SeasonProbes
{
    public static void Register(CommandContext ctx)
    {
        var s = ctx.World.Session.Seasons;
        ctx.AddProbe("season.id", () => s.Config.IndexOf(s.Current));
    }
}
