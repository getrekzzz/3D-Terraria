using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Mount probes: <c>player.mounted</c> (1 riding), <c>mount.flying</c> and <c>mount.flight_left</c> (seconds).</summary>
[ProbeSource]
public static class MountProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("player.mounted", () => ctx.Player.Mount != null ? 1 : 0);
        ctx.AddProbe("mount.flying", () => ctx.Player.Mount is { Flying: true } ? 1 : 0);
        ctx.AddProbe("mount.flight_left", () => ctx.Player.Mount?.FlightLeft ?? 0);
    }
}
