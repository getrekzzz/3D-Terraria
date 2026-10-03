using Strata.Core.Commands;
using Strata.Core.Content;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on generated models: <c>sockets.&lt;item&gt;</c> counts a held item's sockets (grip, muzzle, tip, …);
/// <c>avatar.armor_pieces</c> the armour pieces and accessories worn on the third-person body; <c>avatar.held_lit</c>,
/// <c>avatar.wings_lit</c>, <c>avatar.held_block_cube</c> and <c>view.held_block_cube</c> the P3-06 held-item looks.
/// </summary>
[ProbeSource]
public static class PropProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var content = world.Runtime.Game.Session.Content;
        ctx.AddPrefixProbe("sockets.", id =>
        {
            if (!content.Items.TryGet(id, out var item)) return null;
            var path = AssetPaths.ItemModel(item, content);
            return path == null ? 0 : world.Props.Sockets(path).Count;
        });
        ctx.AddProbe("avatar.armor_pieces", () => world.Player?.GetNodeOrNull<Strata.Game.Player.PlayerAvatar>("Avatar")?.WornCount ?? 0);
        // P3-06: the held item and wings take the body's voxel light; a held block is a textured cube in both views.
        ctx.AddProbe("avatar.held_lit", () => Avatar(world)?.HeldLit == true ? 1 : 0);
        ctx.AddProbe("avatar.wings_lit", () => Avatar(world)?.WingsLit == true ? 1 : 0);
        ctx.AddProbe("avatar.held_block_cube", () => Avatar(world)?.HeldBlockCube == true ? 1 : 0);
        // P3-06: the held model's grip socket lands on its use style's hand frame (cm; -1 without a grip socket).
        ctx.AddProbe("avatar.grip_error_cm", () => Avatar(world)?.GripErrorCm() ?? -1f);
        // The held model hides while a yoyo, flail or whip is out of the hand, in both views; back once it is gone.
        ctx.AddProbe("avatar.held_hidden", () => Avatar(world)?.HeldHidden == true ? 1 : 0);
        ctx.AddProbe("view.held_hidden", () => world.Player?.Rig.Camera.GetNodeOrNull<Strata.Game.Player.ViewModel>("ViewModel")?.HeldHidden == true ? 1 : 0);
        ctx.AddProbe("view.held_block_cube", () => world.Player?.Rig.Camera.GetNodeOrNull<Strata.Game.Player.ViewModel>("ViewModel")?.HeldBlockCube == true ? 1 : 0);
    }

    private static Strata.Game.Player.PlayerAvatar? Avatar(LaunchedWorld world) => world.Player?.GetNodeOrNull<Strata.Game.Player.PlayerAvatar>("Avatar");
}
