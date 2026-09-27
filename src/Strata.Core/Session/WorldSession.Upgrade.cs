using Strata.Core.Content;
using Strata.Core.Persistence;

namespace Strata.Core.Session;

public sealed partial class WorldSession
{
    /// <summary>
    /// Brings ids stored in world.json up to date with renamed content (data/aliases.json): progression flags, events,
    /// town residents and world mutations. Block, item and object ids in regions resolve through the same aliases.
    /// </summary>
    private static void UpgradeIds(WorldMeta meta, ContentDatabase content)
    {
        if (content.Aliases.Count == 0) return;
        meta.Flags = meta.Flags.Select(content.Current).Distinct().ToList();
        meta.ActiveEvents = meta.ActiveEvents.Select(content.Current).ToList();
        meta.EventStates = meta.EventStates.Select(e => e with { Id = content.Current(e.Id) }).ToList();
        meta.Residents = meta.Residents.Select(r => r with { Id = content.Current(r.Id) }).ToList();
        meta.Mutations = meta.Mutations.Select(m => m with { Id = content.Current(m.Id) }).ToList();
    }
}
