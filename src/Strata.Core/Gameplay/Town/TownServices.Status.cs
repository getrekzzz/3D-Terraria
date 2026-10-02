using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Town;

/// <summary>The Dryad's Status: how pure, corrupt (or crimson) and hallowed the world is.</summary>
public sealed partial class TownServices
{
    /// <summary>The status text, or null while the world is still being sampled (ask again shortly).</summary>
    public string? WorldStatusText(TownNpc npc)
    {
        if (npc.Def.FindService(TownServiceSpecs.WorldStatus) is not { } spec) return null;
        var p = spec.Parse<WorldStatusParams>();
        if (_world.Town.Status.Request(p) is not { } report) return null;
        string evilName = _world.Session.Settings.Evil.ToString().ToLowerInvariant();
        return WorldStatus.Describe(report, p, _world.Session.Settings.Name, evilName);
    }
}
