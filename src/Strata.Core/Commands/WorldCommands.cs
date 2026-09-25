using Strata.Core.WorldGen.Mutations;

namespace Strata.Core.Commands;

/// <summary>World progression commands: the mutation log and spreading biomes.</summary>
public static class WorldCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "mutations", Usage = "mutations [catchup | spread <updates>|on|off]",
            Help = "Lists world mutations (hardmode ores, wedges), brings loaded chunks up to date, runs or toggles biome spread.",
            Run = (ctx, a) =>
            {
                var log = ctx.World.Session.Mutations;
                switch (a.OptStr(0))
                {
                    case null:
                        if (log.Level == 0) return CommandResult.Success("No world mutations yet.");
                        var zones = log.Zones.OfType<WedgeStep>().Select(z => $"{z.Zone} wedge at ({z.Anchor.X:0}, {z.Anchor.Z:0}) running ({z.Anchor.DirX:0.00}, {z.Anchor.DirZ:0.00})");
                        return CommandResult.Success($"Mutation level {log.Level}: {string.Join(", ", log.Ids)}; {ctx.World.Mutations.Behind} loaded chunks behind."
                            + (zones.Any() ? "\n" + string.Join("\n", zones) : ""));
                    case "catchup":
                        return CommandResult.Success($"Caught up {ctx.World.Mutations.CatchUpAll()} chunks.");
                    case "spread" when a.OptStr(1) is "on" or "off":
                        ctx.World.Spread.Enabled = a.Str(1, "on|off") == "on";
                        return CommandResult.Success($"Biome spread {(ctx.World.Spread.Enabled ? "on" : "off")}.");
                    case "spread":
                        int converted = 0, updates = Math.Clamp(a.Int(1, "updates"), 1, 1000);
                        for (int i = 0; i < updates; i++) converted += ctx.World.Spread.Update(ctx.World.Spread.Config.CellsPerChunk);
                        return CommandResult.Success($"{converted} blocks converted by spreading.");
                    default:
                        throw new CommandException("unknown mutations subcommand");
                }
            },
        });
    }
}
