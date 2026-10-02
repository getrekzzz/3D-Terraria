using System.Runtime.CompilerServices;

namespace Strata.Core.Content.Index;

/// <summary>
/// Crafting station ids as players read them: the names of the objects that provide the station, cheapest first
/// (then by name), at most two joined by " or " ("anvil" → "Iron Anvil or Lead Anvil"), or just the object named like the
/// station when there is one ("work_bench" → "Work Bench", whatever its wood). Built once per content
/// database and cached; shared by crafting, the Guide and tooltips.
/// </summary>
public static class StationNames
{
    private const int MaxNames = 2;

    private static readonly ConditionalWeakTable<ContentDatabase, Dictionary<string, string>> Cache = new();

    /// <summary>Display text for a station id; an id no object provides reads as words ("hardmode_anvil" → "Hardmode anvil").</summary>
    public static string Describe(string stationId, ContentDatabase content)
    {
        var names = Cache.GetValue(content, Build);
        return names.TryGetValue(stationId, out var text) ? text : Humanize(stationId);
    }

    /// <summary>Several stations, each described, joined with ", " (a recipe needing an anvil and a furnace).</summary>
    public static string Describe(IEnumerable<string> stationIds, ContentDatabase content) =>
        string.Join(", ", stationIds.Select(s => Describe(s, content)));

    private static Dictionary<string, string> Build(ContentDatabase content)
    {
        var providers = new Dictionary<string, List<(long Value, string Name)>>(StringComparer.Ordinal);
        // An object named like the station (the plain Work Bench) speaks for it alone: furniture-family benches are variants.
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var obj in content.TileObjects.All)
        {
            if (obj.Stations.Length == 0 || obj.HasTag("placeholder")) continue;
            long value = content.Items.Find(obj.PlacingItemId)?.Value ?? obj.ItemValue;
            foreach (var station in obj.Stations)
            {
                if (obj.Id == station) canonical[station] = obj.DisplayName;
                if (!providers.TryGetValue(station, out var list)) providers[station] = list = new();
                if (!list.Exists(p => p.Name == obj.DisplayName)) list.Add((value, obj.DisplayName));
            }
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (station, list) in providers)
            result[station] = string.Join(" or ", list
                .OrderBy(p => p.Value).ThenBy(p => p.Name, StringComparer.Ordinal)
                .Take(MaxNames).Select(p => p.Name));
        foreach (var (station, name) in canonical) result[station] = name;
        return result;
    }

    private static string Humanize(string id)
    {
        var words = id.Replace('_', ' ').Trim();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
