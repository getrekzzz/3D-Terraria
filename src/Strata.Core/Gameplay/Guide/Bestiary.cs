using Strata.Core.Content;
using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Guide;

/// <summary>How much of a bestiary entry a character has unlocked.</summary>
public enum BestiaryUnlock
{
    None,

    /// <summary>Seen nearby: the portrait and name.</summary>
    Seen,

    /// <summary>Killed: life, defence and damage.</summary>
    Stats,

    /// <summary>Killed often enough (or caught, or met): everything, drops included.</summary>
    Full,
}

/// <summary>
/// The bestiary: one entry per creature (parts credit their boss through <see cref="EnemyDef.BestiaryAs"/>; creatures
/// tagged "no_bestiary" are left out) and per town resident. Unlocks are per character (the reference game keeps them
/// per world); world-wide conditions such as the Zoologist's take the best present player.
/// </summary>
public sealed class Bestiary
{
    public const string ExcludeTag = "no_bestiary";

    private readonly List<ContentDef> _entries = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _parts = new(StringComparer.Ordinal);

    public Bestiary(ContentDatabase content, BestiaryConfig? config = null)
    {
        Config = config ?? new BestiaryConfig();
        foreach (var e in content.Enemies.All)
            if (e.BestiaryAs == null && !e.HasTag(ExcludeTag) && !e.HasTag("debug") && _ids.Add(e.Id)) _entries.Add(e);
        foreach (var e in content.Enemies.All)
            if (e.BestiaryAs != null)
            {
                if (!_parts.TryGetValue(e.BestiaryAs, out var list)) _parts[e.BestiaryAs] = list = new List<string>();
                list.Add(e.Id);
            }
        foreach (var t in content.TownNpcs.All)
            if (_ids.Add(t.Id)) _entries.Add(t);
    }

    public BestiaryConfig Config { get; set; }

    /// <summary>Every entry: creatures in content order, then town residents.</summary>
    public IReadOnlyList<ContentDef> Entries => _entries;

    public bool Contains(string id) => _ids.Contains(id);

    /// <summary>The entry a creature's kills and sightings credit.</summary>
    public static string EntryOf(EnemyDef def) => def.BestiaryAs ?? def.Id;

    public BestiaryUnlock Unlock(ContentDef entry, PlayerJournal journal)
    {
        if (entry is TownNpcDef) return journal.Met.Contains(entry.Id) ? BestiaryUnlock.Full : BestiaryUnlock.None;
        var enemy = (EnemyDef)entry;
        int kills = Kills(entry.Id, journal);
        if (journal.Caught.Contains(entry.Id)) return BestiaryUnlock.Full;
        int forDrops = enemy.Boss is { Minor: false } ? Config.BossKillsForDrops : enemy.HasTag("rare") ? Config.RareKillsForDrops : Config.KillsForDrops;
        // Critters give everything away on the first kill, as in the reference game.
        if (kills >= forDrops || (kills > 0 && enemy.HasTag("critter"))) return BestiaryUnlock.Full;
        if (kills >= Config.KillsForStats) return BestiaryUnlock.Stats;
        return journal.Seen.Contains(entry.Id) ? BestiaryUnlock.Seen : BestiaryUnlock.None;
    }

    /// <summary>Kills that count for an entry: its own plus those of the parts that credit it.</summary>
    public int Kills(string entry, PlayerJournal journal)
    {
        int kills = journal.Kills(entry);
        if (_parts.TryGetValue(entry, out var parts))
            foreach (var part in parts) kills += journal.Kills(part);
        return kills;
    }

    /// <summary>Completion 0–100: a seen entry counts half, stats or more a whole one.</summary>
    public float Percent(PlayerJournal journal)
    {
        if (_entries.Count == 0) return 0f;
        float sum = 0f;
        foreach (var e in _entries)
            sum += Unlock(e, journal) switch { BestiaryUnlock.None => 0f, BestiaryUnlock.Seen => 0.5f, _ => 1f };
        return sum * 100f / _entries.Count;
    }
}
