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
        ctx.AddProbe("town.petted", () => w.Town.PetCount);
        ctx.AddProbe("town.emotes_shown", () => w.Town.EmotesShown);
        ctx.AddProbe("town.pylons", () => w.Town.Pylons.All().Count);
        ctx.AddProbe("town.pylon_teleports", () => w.Town.Pylons.Teleports);
        // town.<id>: 1 when the resident lives in town; town.<id>.home: metres from their home (−1 if homeless/absent);
        // town.<id>.alive: 1 while they are alive; town.<id>.homed: 1 while they have a home.
        ctx.AddPrefixProbe("town.", spec =>
        {
            var parts = spec.Split('.');
            if (!w.Session.Content.TownNpcs.Contains(parts[0])) return null;
            var t = w.Town.Find(parts[0]);
            if (parts.Length == 1) return t != null ? 1 : 0;
            if (parts is [_, "home"]) return t?.Home is { } h ? Vector3.Distance(t.Position, TownSystem.NodeFeet(h)) : -1;
            if (parts is [_, "alive"]) return t is { Alive: true } ? 1 : 0;
            if (parts is [_, "homed"]) return t?.Home != null ? 1 : 0;
            if (parts is [_, "bound"]) return t is { Bound: true } ? 1 : 0;
            if (parts is [_, "sitting"]) return t is { Posture: ResidentPosture.Sit or ResidentPosture.Sleep } ? 1 : 0;
            if (parts is [_, "attacks"]) return t?.Attacks ?? 0;
            if (parts is [_, "posture"]) return t != null ? (int)t.Posture : -1; // ResidentPosture: 0 stand, 1 sit, 2 sleep, 3 bound, 4 lie
            return null;
        });
    }
}
