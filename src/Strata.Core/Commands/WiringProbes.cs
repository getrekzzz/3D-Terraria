using Strata.Core.Gameplay.Wiring;

namespace Strata.Core.Commands;

/// <summary>
/// Wiring probes: <c>wires.red|blue|green|yellow</c> (cells of that colour), <c>wires.actuators</c>,
/// <c>wires.inactive</c> (blocks switched off), <c>wires.teleports</c> (players teleported so far),
/// <c>wires.statue_npcs</c> (statue creatures alive). <c>wires.count</c> is a standard probe.
/// </summary>
[ProbeSource]
internal static class WiringProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World.Wires;
        foreach (var color in WireColors.All)
        {
            var c = color;
            ctx.AddProbe("wires." + c.ToString().ToLowerInvariant(), () => Count(w.Grid, (byte)c));
        }
        ctx.AddProbe("wires.actuators", () => Count(w.Grid, WireGrid.ActuatorBit));
        ctx.AddProbe("wires.inactive", () => w.Grid.Inactive.Count);
        ctx.AddProbe("wires.teleports", () => w.Teleports);
        ctx.AddProbe("wires.statue_npcs", () => ctx.World.Npcs.All.Count(n => n.Alive && n.FromStatue));
    }

    private static int Count(WireGrid grid, byte bit)
    {
        int n = 0;
        foreach (var m in grid.Cells.Values)
            if ((m & bit) != 0) n++;
        return n;
    }
}
