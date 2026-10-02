using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Probes on the town's own panels: whether the haircut (P5-21) is open.</summary>
[ProbeSource]
public static class TownUiProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddProbe("ui.haircut_open", () => world.TownPanels?.Haircut.IsOpen == true ? 1 : 0);
    }
}
