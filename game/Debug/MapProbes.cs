using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Probes on the map (W4-POLISH): the map pixels this player has discovered, the share of the minimap showing
/// discovered ground, and the map's markers drawn as icons (the spawn flag, the bed, resident and boss heads).
/// </summary>
[ProbeSource]
public static class MapProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("map.revealed_cells", () => ctx.World.Maps.Explored(ctx.Player));
        ctx.AddProbe("map.minimap_coverage", () => world.Runtime.GetNodeOrNull<Minimap>("Minimap")?.Coverage ?? 0);
        if (world.WorldMap is { } map) ctx.AddProbe("map.marker_icons", () => map.MarkerIcons());
    }
}
