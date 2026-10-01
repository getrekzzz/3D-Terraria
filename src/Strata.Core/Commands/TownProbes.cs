using System.Numerics;
using Strata.Core.Gameplay.Town;

namespace Strata.Core.Commands;

/// <summary>Town probes: how many residents live in town and have a home, and where each resident is.</summary>
[ProbeSource]
internal static class TownProbes
{
    public static void Register(CommandContext ctx)
    {
        var w = ctx.World;
        ctx.AddProbe("town.residents", () => w.Town.Residents.Count);
        ctx.AddProbe("town.homed", () => w.Town.Residents.Count(t => t.Home != null));
        // town.<id>: 1 when the resident lives in town; town.<id>.home: metres from their home (−1 if homeless/absent);
        // town.<id>.alive: 1 while they are alive.
        ctx.AddPrefixProbe("town.", spec =>
        {
            var parts = spec.Split('.');
            if (!w.Session.Content.TownNpcs.Contains(parts[0])) return null;
            var t = w.Town.Find(parts[0]);
            if (parts.Length == 1) return t != null ? 1 : 0;
            if (parts is [_, "home"]) return t?.Home is { } h ? Vector3.Distance(t.Position, TownSystem.NodeFeet(h)) : -1;
            if (parts is [_, "alive"]) return t is { Alive: true } ? 1 : 0;
            return null;
        });
    }
}
