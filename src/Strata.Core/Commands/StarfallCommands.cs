namespace Strata.Core.Commands;

/// <summary>
/// <c>starfall now</c>: drops a Fallen Star near the player at once, day or night (P3-30). Probes <c>stars.fallen</c>,
/// <c>stars.faded</c>, <c>stars.active</c> and <c>drops.fallen_star</c> (Fallen Star items lying on the ground).
/// </summary>
public static class StarfallCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "starfall", Usage = "starfall now",
            Help = "Drops a Fallen Star from the sky near the player (it must be on the surface under loaded sky).",
            Run = (ctx, a) =>
            {
                if (a.Str(0, "now") != "now") throw new CommandException("usage: starfall now");
                return ctx.World.Starfall.FallNear(ctx.Player) is { } star
                    ? CommandResult.Success($"A star falls from {star.Position.Y:0.0} m.")
                    : CommandResult.Fail("No star could fall here (underground, unloaded sky, or too many already falling).");
            },
        });
    }
}

[ProbeSource]
internal static class StarfallProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddProbe("stars.fallen", () => w.Starfall.Fallen);
        ctx.AddProbe("stars.faded", () => w.Starfall.Faded);
        ctx.AddProbe("stars.active", () => w.Projectiles.All.Count(p => p.Def.Id == w.Starfall.Config.Projectile));
        ctx.AddProbe("drops.fallen_star", () => w.Drops.Items.Where(d => d.Stack.Item?.Id == "fallen_star").Sum(d => d.Stack.Count));
    }
}
