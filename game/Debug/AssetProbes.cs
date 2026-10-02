using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Rendering;

namespace Strata.Game.Debug;

/// <summary>Probes on the threaded asset cache: <c>assets.pending</c> loads in flight, <c>assets.loaded</c> held.</summary>
[ProbeSource]
public static class AssetProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("assets.pending", () => AssetCache.PendingCount);
        ctx.AddProbe("assets.loaded", () => AssetCache.LoadedCount);
    }
}
