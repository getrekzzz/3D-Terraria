using Strata.Core.Commands;
using Strata.Core.Content;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on generated models: <c>sockets.&lt;item&gt;</c> counts a held item's sockets (grip, muzzle, tip, …);
/// <c>avatar.armor_pieces</c> the armour pieces and accessories worn on the third-person body.
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
    }
}
