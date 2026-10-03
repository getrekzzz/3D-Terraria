using Strata.Core.Gameplay.Guide;

namespace Strata.Core.Persistence;

/// <summary>
/// A <see cref="PlayerJournal"/> as saved: ids stay plain strings (unknown ones are kept, so removed content loses
/// nothing), lists sorted for stable files.
/// </summary>
public sealed class JournalModel
{
    public Dictionary<string, int> Kills { get; set; } = new();
    public List<string> Seen { get; set; } = new();
    public List<string> Caught { get; set; } = new();
    public List<string> Met { get; set; } = new();
    public List<string> Obtained { get; set; } = new();
    public List<string> Crafted { get; set; } = new();
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, long> Achievements { get; set; } = new();

    /// <summary>Onboarding hints already shown (additive; absent in older saves).</summary>
    public List<string> Hints { get; set; } = new();

    public static JournalModel From(PlayerJournal j) => new()
    {
        Kills = new Dictionary<string, int>(j.KillCounts),
        Seen = Sorted(j.Seen),
        Caught = Sorted(j.Caught),
        Met = Sorted(j.Met),
        Obtained = Sorted(j.Obtained),
        Crafted = Sorted(j.Crafted),
        Counters = new Dictionary<string, long>(j.Counters),
        Achievements = new Dictionary<string, long>(j.Achievements),
        Hints = Sorted(j.HintsShown),
    };

    /// <summary>Loads into an (empty) journal without raising its change events; null collections are skipped.</summary>
    public void Fill(PlayerJournal j)
    {
        if (Kills != null)
            foreach (var kv in Kills) if (kv.Key != null && kv.Value > 0) j.KillCounts[kv.Key] = kv.Value;
        Add(j.Seen, Seen);
        Add(j.Caught, Caught);
        Add(j.Met, Met);
        Add(j.Obtained, Obtained);
        Add(j.Crafted, Crafted);
        if (Counters != null)
            foreach (var kv in Counters) if (kv.Key != null) j.Counters[kv.Key] = kv.Value;
        if (Achievements != null)
            foreach (var kv in Achievements) if (kv.Key != null) j.Achievements[kv.Key] = kv.Value;
        Add(j.HintsShown, Hints);
    }

    private static List<string> Sorted(HashSet<string> set)
    {
        var list = new List<string>(set);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void Add(HashSet<string> into, List<string>? from)
    {
        if (from == null) return;
        foreach (var id in from) if (!string.IsNullOrEmpty(id)) into.Add(id);
    }
}
