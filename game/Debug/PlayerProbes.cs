using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Player;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the player's physical body (floor contact, the terrain-loading freeze, flight time, the hook claw's snap
/// onto the rendered surface; the hook's state is a Core probe), on how far the third-person body turns from the
/// crosshair, how its item actions blend and how it holds its item, on the first-person view model (a bow's nocked
/// arrow, a ranged weapon's aim) and on the crosshair target.
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
        // How far (m) the hook's claw was moved from its voxel hit onto the rendered surface; -1 when none was found.
        ctx.AddProbe("player.hook_snap", () => runtime.GetNodeOrNull<GrappleVisual>("GrappleVisual")?.SnapDistance ?? -1);
        if (world.Avatar is { } avatar)
        {
            ctx.AddProbe("player.facing_error_deg", () => avatar.FacingErrorDegrees);
            ctx.AddProbe("player.stride", () => (int)avatar.Stride); // 0 forward, 1 strafe left, 2 strafe right, 3 backpedal
            ctx.AddProbe("player.twist_deg", () => avatar.TwistDegrees);
            ctx.AddProbe("player.action_dips", () => avatar.ActionDips); // chained uses that faded toward locomotion between
            ctx.AddProbe("player.action_weight", () => avatar.ActionWeight);
            // The item in the third-person hand: its limbs' or shaft's lean from upright, its business end's angle off the aim.
            ctx.AddProbe("player.held_tilt_deg", () => avatar.HeldDegrees().Tilt);
            ctx.AddProbe("player.held_aim_deg", () => avatar.HeldDegrees().Aim);
        }
        if (player.Rig.Camera.GetNodeOrNull<ViewModel>("ViewModel") is { } viewModel)
        {
            // First person: the bow's nocked arrow (1 on the string, 0 loosed, -1 none) and how far a bow or gun points
            // off the crosshair point.
            ctx.AddProbe("fp.arrow", () => viewModel.ArrowState);
            ctx.AddProbe("fp.aim_error_deg", () => viewModel.AimErrorDegrees);
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
