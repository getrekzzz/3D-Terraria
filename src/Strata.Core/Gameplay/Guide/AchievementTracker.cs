using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Guide;

/// <summary>
/// Unlocks achievements per character. A journal change evaluates only the achievements whose trigger watches that
/// kind of change; world-state triggers (flags, housing, bestiary, coins, layer) are swept once a second. A player's
/// first sweep evaluates everything, so achievements earned before a trigger existed unlock on load.
/// </summary>
public sealed class AchievementTracker
{
    private readonly GameWorld _world;
    private readonly List<(AchievementDef Def, GuideTrigger Trigger)> _all = new();
    private readonly List<(AchievementDef Def, GuideTrigger Trigger)> _swept = new();
    private readonly Dictionary<JournalKind, List<(AchievementDef Def, GuideTrigger Trigger)>> _byKind = new();
    private readonly Dictionary<PlayerJournal, Action<JournalKind, string>> _attached = new();

    public AchievementTracker(GameWorld world)
    {
        _world = world;
        foreach (var def in world.Session.Content.Achievements.All)
        {
            GuideTrigger trigger;
            try { trigger = GuideTrigger.Compile(def.Trigger); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
            {
                Diagnostics.LogExtensions.Warn(world.Session.Log, "guide", $"achievement {def.Id}: {e.Message}"); // the validator reports it with context
                continue;
            }
            _all.Add((def, trigger));
            if (trigger.Watch is { } kind)
            {
                if (!_byKind.TryGetValue(kind, out var list)) _byKind[kind] = list = new();
                list.Add((def, trigger));
            }
            else _swept.Add((def, trigger));
        }
    }

    /// <summary>A player unlocked an achievement (the popup and its sound listen).</summary>
    public event Action<PlayerEntity, AchievementDef>? Unlocked;

    /// <summary>Progress towards an achievement for the book: (current, target).</summary>
    public (long Current, long Target) Progress(AchievementDef def, PlayerEntity player)
    {
        foreach (var (d, t) in _all)
            if (d == def) return player.State.Journal.Achieved(def.Id) ? (t.Target, t.Target) : (t.Current(_world, player), t.Target);
        return (0, 1);
    }

    /// <summary>Unlocks an achievement directly (the <c>achievement grant</c> command); false when already unlocked.</summary>
    public bool Grant(PlayerEntity player, AchievementDef def)
    {
        if (!player.State.Journal.Unlock(def.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) return false;
        Unlocked?.Invoke(player, def);
        return true;
    }

    public void Tick()
    {
        if (_world.Session.Clock.Tick % Units.TicksPerSecond != 0) return;
        foreach (var p in _world.Players)
        {
            if (!_attached.ContainsKey(p.State.Journal))
            {
                Attach(p);
                Evaluate(p, _all);
            }
            else Evaluate(p, _swept);
        }
    }

    /// <summary>Stops listening to every journal (the world is closing; the characters may join another).</summary>
    public void Detach()
    {
        foreach (var (journal, handler) in _attached) journal.Changed -= handler;
        _attached.Clear();
    }

    private void Attach(PlayerEntity p)
    {
        Action<JournalKind, string> handler = (kind, _) =>
        {
            if (_byKind.TryGetValue(kind, out var list)) Evaluate(p, list);
        };
        p.State.Journal.Changed += handler;
        _attached[p.State.Journal] = handler;
    }

    private void Evaluate(PlayerEntity p, List<(AchievementDef Def, GuideTrigger Trigger)> list)
    {
        var j = p.State.Journal;
        for (int i = 0; i < list.Count; i++)
        {
            var (def, trigger) = list[i];
            if (!j.Achieved(def.Id) && trigger.Met(_world, p)) Grant(p, def);
        }
    }
}
