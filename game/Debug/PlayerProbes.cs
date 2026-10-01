using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Player;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the player's physical body (floor contact, the terrain-loading freeze, flight time; the hook is a Core
/// probe), on how far the third-person body turns from the crosshair, and on the crosshair target.
/// </summary>
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
        ctx.AddProbe("player.flight_left", () => player.FlightLeft);
        ctx.AddProbe("player.area_ready", () => runtime.IsAreaReady(PlayerController.CellAt(player.GlobalPosition)) ? 1 : 0);
        if (world.Avatar is { } avatar)
        {
            ctx.AddProbe("player.facing_error_deg", () => avatar.FacingErrorDegrees);
            ctx.AddProbe("player.stride", () => (int)avatar.Stride); // 0 forward, 1 strafe left, 2 strafe right, 3 backpedal
            ctx.AddProbe("player.twist_deg", () => avatar.TwistDegrees);
        }
        if (world.Interaction is not { } interaction) return;
        // 1 when there is no target or it lies ahead of the eye along the facing: nothing behind the player is selected.
        ctx.AddProbe("target.front", () =>
        {
            if (interaction.Target is not { } t) return 1;
            var entity = player.Entity;
            var point = t.HitCells * Strata.Core.Common.Units.CellSize;
            return System.Numerics.Vector3.Dot(point - entity.Eye, entity.Facing) > -1e-3f ? 1 : 0;
        });
        ctx.AddProbe("target.dist", () => interaction.Target?.Distance ?? -1);
        ctx.AddProbe("player.aim_distance", () => player.Entity.AimDistance); // eye to the crosshair point shots converge on
    }
}
