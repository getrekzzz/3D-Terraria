namespace Strata.Core.Gameplay.Guide;

/// <summary>What a journal entry records (the key is a content id or a counter name).</summary>
public enum JournalKind
{
    Kill,
    Seen,
    Caught,
    Met,
    Obtained,
    Crafted,
    Counter,
    Achievement,
}

/// <summary>What a character has done, as other systems (town arrivals, achievements, the guide) ask about it.</summary>
public interface IPlayerRecords
{
    int Kills(string enemy);
    bool HasObtained(string item);
    bool HasCrafted(string item);
    bool HasMet(string resident);
    long Counter(string key);

    /// <summary>Bestiary completion 0–100.</summary>
    float BestiaryPercent { get; }

    bool Achieved(string achievement);
}

/// <summary>
/// A character's journal (saved with the player, not the world): creatures seen, killed and caught, townsfolk met,
/// items obtained and crafted, named counters and unlocked achievements. Ids stay strings, so removing content never
/// loses a record. Recording an existing key allocates nothing; <see cref="Changed"/> fires only on news (and on every
/// kill and counter step).
/// </summary>
public sealed class PlayerJournal : IPlayerRecords
{
    private float _percent = -1f;

    public Dictionary<string, int> KillCounts { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Caught { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Met { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Obtained { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Crafted { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);

    /// <summary>Unlocked achievements and when (Unix seconds).</summary>
    public Dictionary<string, long> Achievements { get; } = new(StringComparer.Ordinal);

    /// <summary>The bestiary that <see cref="BestiaryPercent"/> is measured against (set when the player joins a world).</summary>
    public Bestiary? Bestiary { get; set; }

    public event Action<JournalKind, string>? Changed;

    public void AddKill(string enemy)
    {
        KillCounts.TryGetValue(enemy, out int n);
        KillCounts[enemy] = n + 1;
        _percent = -1f;
        Changed?.Invoke(JournalKind.Kill, enemy);
    }

    public bool MarkSeen(string enemy) => Mark(Seen, JournalKind.Seen, enemy, true);
    public bool MarkCaught(string critter) => Mark(Caught, JournalKind.Caught, critter, true);
    public bool MarkMet(string resident) => Mark(Met, JournalKind.Met, resident, true);
    public bool MarkObtained(string item) => Mark(Obtained, JournalKind.Obtained, item, false);
    public bool MarkCrafted(string item) => Mark(Crafted, JournalKind.Crafted, item, false);

    public void Count(string key, long by = 1)
    {
        Counters.TryGetValue(key, out long n);
        Counters[key] = n + by;
        Changed?.Invoke(JournalKind.Counter, key);
    }

    /// <summary>Records an achievement; false when it was already unlocked.</summary>
    public bool Unlock(string achievement, long unixSeconds)
    {
        if (!Achievements.TryAdd(achievement, unixSeconds)) return false;
        Changed?.Invoke(JournalKind.Achievement, achievement);
        return true;
    }

    /// <summary>Forgets everything (the <c>journal reset</c> command).</summary>
    public void Clear()
    {
        KillCounts.Clear();
        Seen.Clear();
        Caught.Clear();
        Met.Clear();
        Obtained.Clear();
        Crafted.Clear();
        Counters.Clear();
        Achievements.Clear();
        _percent = -1f;
    }

    public int Kills(string enemy) => KillCounts.TryGetValue(enemy, out int n) ? n : 0;
    public bool HasObtained(string item) => Obtained.Contains(item);
    public bool HasCrafted(string item) => Crafted.Contains(item);
    public bool HasMet(string resident) => Met.Contains(resident);
    public long Counter(string key) => Counters.TryGetValue(key, out long n) ? n : 0;
    public bool Achieved(string achievement) => Achievements.ContainsKey(achievement);

    public float BestiaryPercent
    {
        get
        {
            if (Bestiary == null) return 0f;
            if (_percent < 0f) _percent = Bestiary.Percent(this);
            return _percent;
        }
    }

    private bool Mark(HashSet<string> set, JournalKind kind, string key, bool bestiary)
    {
        if (!set.Add(key)) return false;
        if (bestiary) _percent = -1f;
        Changed?.Invoke(kind, key);
        return true;
    }
}
