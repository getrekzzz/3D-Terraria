using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Effect probes: <c>effects.bursts</c> (EffectKit bursts started so far) and <c>effects.gore</c> (gore chunks alive).</summary>
[ProbeSource]
public static class EffectProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("effects.bursts", () => Rendering.EffectKit.Instance?.Bursts ?? 0);
        ctx.AddProbe("effects.gore", () => Entities.GoreSystem.Instance?.Live ?? 0);
    }
}
