using System.Globalization;
using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Index;
using Strata.Core.Gameplay.Crafting;

namespace Strata.Core.Gameplay.Guide;

/// <summary>One "Obtained from" row of the Guide book: what, how likely and when; <see cref="Link"/> is the enemy or
/// item the row jumps to.</summary>
public sealed record SourceView(ItemSourceKind Kind, string Title, string Detail, ContentDef? Link);

/// <summary>
/// Everything the Guide book's Items tab reads: every item sorted by name with search keys, where each comes from
/// (<see cref="ItemSourceIndex"/> with drop chances), the recipes making and using it, and how many recipes it serves
/// as a station. Content is immutable, so <see cref="BuildAsync"/> builds it on a worker thread at world launch; it
/// owns its own <see cref="RecipeBook"/> so nothing is shared with the simulation while building.
/// </summary>
public sealed class GuideIndex
{
    private readonly ItemDef[] _items;
    private readonly string[] _keys;
    private readonly Dictionary<string, SourceView[]> _sources = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private GuideIndex(ContentDatabase content)
    {
        Content = content;
        Sources = ItemSourceIndex.Build(content);
        Recipes = new RecipeLookup(new RecipeBook(content), content);
        _items = content.Items.All.Where(i => !i.HasTag("placeholder"))
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        _keys = new string[_items.Length];
        for (int i = 0; i < _items.Length; i++)
        {
            var it = _items[i];
            _keys[i] = (it.DisplayName + " " + it.Id.Replace('_', ' ') + " " + string.Join(' ', it.Tags) + " " + it.Category)
                .ToLower(CultureInfo.InvariantCulture);
        }
    }

    public ContentDatabase Content { get; }
    public ItemSourceIndex Sources { get; }
    public RecipeLookup Recipes { get; }
    public IReadOnlyList<ItemDef> Items => _items;

    public static GuideIndex Build(ContentDatabase content) => new(content);

    public static Task<GuideIndex> BuildAsync(ContentDatabase content) => Task.Run(() => Build(content));

    /// <summary>Items whose name, id, tags or category contain every word of the query, in name order.</summary>
    public List<ItemDef> Search(string? query, ItemCategory? category = null)
    {
        var words = (query ?? "").ToLower(CultureInfo.InvariantCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<ItemDef>();
        for (int i = 0; i < _items.Length; i++)
        {
            if (category is { } c && _items[i].Category != c) continue;
            bool all = true;
            foreach (var w in words)
                if (!_keys[i].Contains(w, StringComparison.Ordinal)) { all = false; break; }
            if (all) result.Add(_items[i]);
        }
        return result;
    }

    /// <summary>The item's "Obtained from" rows (recipes excluded: the book lists them as "Crafted from"), cached.</summary>
    public IReadOnlyList<SourceView> SourcesOf(ItemDef item)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(item.Id, out var cached)) return cached;
            var rows = Sources.For(item.Id).Where(s => s.Kind != ItemSourceKind.Recipe).Select(Describe)
                .OrderBy(r => r.Kind).ThenBy(r => r.Title, StringComparer.Ordinal).ToArray();
            _sources[item.Id] = rows;
            return rows;
        }
    }

    /// <summary>One source as the book reads it.</summary>
    public SourceView Describe(ItemSource s)
    {
        string owner = s.Owner?.DisplayName ?? s.Label ?? "";
        string title = s.Kind switch
        {
            ItemSourceKind.Recipe => "Crafted",
            ItemSourceKind.Drop or ItemSourceKind.BossDrop => s.Owner != null ? owner : s.Label ?? "Any enemy",
            ItemSourceKind.Container => s.Label ?? owner,
            ItemSourceKind.Object => "Breaking " + owner,
            ItemSourceKind.Block => "Mining " + owner,
            ItemSourceKind.Catch => "Catching " + owner,
            ItemSourceKind.Shop => "Sold by the " + owner,
            ItemSourceKind.Fishing => "Fishing",
            ItemSourceKind.Opened => "Opening " + owner,
            ItemSourceKind.AnglerReward => "Angler quest reward",
            ItemSourceKind.Bucket => "Filling a bucket with " + owner,
            _ => s.Label switch { "starter" => "Starting kit", "world" => "Found in the world", var l => l ?? owner },
        };
        var parts = new List<string>(3);
        if (s.Chance is { } chance) parts.Add(Percent(chance));
        if (s.Max > 1) parts.Add(s.Min == s.Max ? $"×{s.Min}" : $"×{s.Min}–{s.Max}");
        var cond = ConditionDescriber.Describe(s.Conditions, Content);
        if (cond.Length > 0) parts.Add(cond);
        return new SourceView(s.Kind, title, string.Join(" · ", parts), s.Owner is EnemyDef or ItemDef ? s.Owner : null);
    }

    /// <summary>A chance as the reference game's bestiary prints it ("100%", "33.33%", "0.5%", "&lt;0.01%").</summary>
    public static string Percent(float chance)
    {
        float pct = chance * 100f;
        if (pct >= 99.995f) return "100%";
        if (pct < 0.01f) return "<0.01%";
        return pct.ToString(pct >= 10f ? "0.##" : pct >= 1f ? "0.##" : "0.###", CultureInfo.InvariantCulture) + "%";
    }
}
