using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Player;

namespace Strata.Game.Debug;

/// <summary>Probes on the player's physical body: floor contact, the terrain-loading freeze, the hook and flight time.</summary>
[ProbeSource]
public static class PlayerProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var player = world.Player;
        if (player == null) return; // free-camera worlds have no player body
        var runtime = world.Runtime;
        ctx.AddProbe("player.on_floor", () => player.IsOnFloor() ? 1 : 0);
        ctx.AddProbe("player.frozen", () => player.Frozen ? 1 : 0);
        ctx.AddProbe("player.hook", () => (int)player.Hook);
        ctx.AddProbe("player.flight_left", () => player.FlightLeft);
        ctx.AddProbe("player.area_ready", () => runtime.IsAreaReady(PlayerController.CellAt(player.GlobalPosition)) ? 1 : 0);
    }
}
