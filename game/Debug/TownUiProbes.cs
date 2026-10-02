using Strata.Core.Commands;
using Strata.Game.Bootstrap;

namespace Strata.Game.Debug;

/// <summary>Probes on the town's own panels: whether the haircut (P5-21) and the housing menu (P5-29) are open, and the banners shown.</summary>
[ProbeSource]
public static class TownUiProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world, GodotLog log)
    {
        ctx.AddProbe("ui.haircut_open", () => world.TownPanels?.Haircut.IsOpen == true ? 1 : 0);
        ctx.AddProbe("ui.housing_open", () => world.TownPanels?.Housing.IsOpen == true ? 1 : 0);
        ctx.AddProbe("ui.housing_banners", () => world.TownPanels?.Housing.BannerCount ?? 0);
        ctx.AddProbe("ui.housing_room_banners", () => world.TownPanels?.Banners.Shown ?? 0);
    }
}
