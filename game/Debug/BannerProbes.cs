using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Enemy banner probes: <c>banners.near</c> (creatures the banners near the player work against),
/// <c>banners.dropped</c> (banners dropped this session).</summary>
[ProbeSource]
public static class BannerProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var game = world.Runtime.Game;
        ctx.AddProbe("banners.near", () => game.LocalPlayer is { } p ? game.Banners.Near(p).Count : 0);
        ctx.AddProbe("banners.dropped", () => game.Banners.Dropped);
    }
}
