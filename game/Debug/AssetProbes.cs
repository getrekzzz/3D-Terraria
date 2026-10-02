using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.Rendering;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the threaded asset cache (<c>assets.pending</c> loads in flight, <c>assets.loaded</c> held), preloading
/// (<c>assets.preloaded</c> requests, <c>npcs.prewarmed</c> pooled visuals) and the shader warm-up (<c>render.warmed</c>).
/// </summary>
[ProbeSource]
public static class AssetProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("assets.pending", () => AssetCache.PendingCount);
        ctx.AddProbe("assets.loaded", () => AssetCache.LoadedCount);
        ctx.AddProbe("assets.preloaded", () => world.Runtime.GetNodeOrNull<AssetPreloader>("AssetPreloader")?.Requested ?? 0);
        ctx.AddProbe("npcs.prewarmed", () => world.Runtime.GetNodeOrNull<Entities.NpcRenderer>("Creatures")?.PrewarmedCount ?? 0);
        ctx.AddProbe("render.warmed", () => ShaderWarmup.Warmed);
    }
}
