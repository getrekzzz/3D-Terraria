using Strata.Core.Commands;
using Strata.Game.Items;

namespace Strata.Game.Debug;

/// <summary>Dropped-item visual probes (P3-36): <c>dropvis.models</c>, <c>dropvis.cubes</c> and <c>dropvis.billboards</c>
/// (drops drawn as 3D models, block cubes or icon billboards), <c>dropvis.halos</c> (rarity or glow halos shown) and
/// <c>dropvis.lit</c> (drops that may claim a dynamic light, capped by the preset's dropLights).</summary>
[ProbeSource]
public static class DropProbes
{
    public static void Register(CommandContext ctx)
    {
        ctx.AddProbe("dropvis.models", () => DroppedItemRenderer.Instance?.Counts().Models ?? 0);
        ctx.AddProbe("dropvis.cubes", () => DroppedItemRenderer.Instance?.Counts().Cubes ?? 0);
        ctx.AddProbe("dropvis.billboards", () => DroppedItemRenderer.Instance?.Counts().Billboards ?? 0);
        ctx.AddProbe("dropvis.halos", () => DroppedItemRenderer.Instance?.Counts().Halos ?? 0);
        ctx.AddProbe("dropvis.lit", () => DroppedItemRenderer.Instance?.Counts().Lit ?? 0);
    }
}
