using Strata.Core.Gameplay.Fishing;

namespace Strata.Core.Commands;

/// <summary><c>fishing</c>: shows the player's fishing power, hooks a fish now, or makes every catch use up its bait.</summary>
public static class FishingCommands
{
    public static void Register(CommandRegistry r)
    {
        FishingConfig? loaded = null;
        r.Register(new CommandDef
        {
            Name = "fishing", Usage = "fishing [bite | bait always|normal]",
            Help = "Shows the fishing power at the bobber; 'bite' hooks a fish on the floating bobber now; 'bait always' makes every catch use up its bait (scripts).",
            Run = (ctx, a) =>
            {
                var projectiles = ctx.World.Projectiles;
                var bobber = projectiles.BobberOf(ctx.Player);
                switch (a.OptStr(0))
                {
                    case null:
                        if (bobber == null) return CommandResult.Success("No bobber out.");
                        return CommandResult.Success($"Fishing power {projectiles.FishingPowerFor(ctx.Player, bobber.Source, bobber.PoolCells)} "
                            + $"in {bobber.FishLiquid} ({bobber.PoolCells} cells){(bobber.Biting ? ", biting" : "")}.");
                    case "bite":
                        if (bobber is not { Floating: true }) throw new CommandException("the bobber is not floating");
                        if (bobber.FishingPower <= 0) throw new CommandException("nothing can bite (no bait or too little liquid)");
                        bobber.Biting = true;
                        bobber.BiteAt = bobber.Age + 600;
                        return CommandResult.Success("Something bites!");
                    case "bait":
                        loaded ??= projectiles.Fishing;
                        bool always = a.Str(1, "always|normal") == "always";
                        projectiles.Fishing = always ? loaded with { BaitDivisor = 1e9f } : loaded;
                        return CommandResult.Success(always ? "Every catch now uses up its bait." : "Bait is used up normally.");
                    default:
                        throw new CommandException("usage: fishing [bite | bait always|normal]");
                }
            },
        });
    }
}

/// <summary>
/// Fishing probes: <c>fishing.bobber</c> (0 none, 1 flying, 2 floating, 3 biting), <c>fishing.power</c> (at the bobber,
/// 0 without one), <c>fishing.pool</c> (cells), <c>fishing.catches</c> (landed this session).
/// </summary>
[ProbeSource]
internal static class FishingProbes
{
    public static void Register(CommandContext ctx)
    {
        var p = ctx.World.Projectiles;
        ctx.AddProbe("fishing.bobber", () => p.BobberOf(ctx.Player) is not { } b ? 0 : b.Biting ? 3 : b.Floating ? 2 : 1);
        ctx.AddProbe("fishing.power", () => p.BobberOf(ctx.Player) is { } b ? p.FishingPowerFor(ctx.Player, b.Source, b.PoolCells) : 0);
        ctx.AddProbe("fishing.pool", () => p.BobberOf(ctx.Player)?.PoolCells ?? 0);
        ctx.AddProbe("fishing.catches", () => p.Catches);
    }
}
