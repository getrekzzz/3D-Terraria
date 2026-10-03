using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Index;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>One ingredient as shown: its label ("Wood", "Any Wood"), the item whose icon stands for it, the count and,
/// for a group, every item it accepts.</summary>
public sealed record IngredientView(string Label, ItemDef Icon, int Count, IReadOnlyList<ItemDef> Accepts);

/// <summary>A recipe as the Guide shows it: output and count, ingredients, named stations and condition text.</summary>
public sealed record RecipeView(CompiledRecipe Recipe, ItemDef Output, int Count, IReadOnlyList<IngredientView> Ingredients,
    IReadOnlyList<string> Stations, string Conditions)
{
    /// <summary>"Required objects: Iron Anvil or Lead Anvil, Furnace" (empty when crafted by hand).</summary>
    public string StationLine => Stations.Count == 0 ? "" : "Required objects: " + string.Join(", ", Stations);
}

/// <summary>
/// Recipe views for the Guide (book Items tab, the Guide's crafting help): every recipe making or using an item, with
/// readable ingredients and stations. Built over the <see cref="RecipeBook"/> indexes; views are cached per recipe,
/// so the lookup is cheap after the first query and safe to build off the main thread (content is immutable).
/// </summary>
public sealed class RecipeLookup
{
    private readonly RecipeBook _book;
    private readonly ContentDatabase _content;
    private readonly Dictionary<CompiledRecipe, RecipeView> _views = new();
    private readonly object _gate = new();

    public RecipeLookup(RecipeBook book, ContentDatabase content)
    {
        _book = book;
        _content = content;
    }

    /// <summary>Recipes that make the item.</summary>
    public IReadOnlyList<RecipeView> Making(ItemDef item) => Views(_book.RecipesFor(item));

    /// <summary>Recipes that use the item as an ingredient (directly or through a group).</summary>
    public IReadOnlyList<RecipeView> Using(ItemDef item) => Views(_book.RecipesUsing(item));

    /// <summary>Recipes needing a station the item provides when placed (an anvil → every anvil recipe).</summary>
    public int StationFor(ItemDef item)
    {
        if (item.PlaceObject is not { } objId || !_content.TileObjects.TryGet(objId, out var obj) || obj.Stations.Length == 0) return 0;
        int n = 0;
        foreach (var r in _book.All)
            foreach (var st in r.Def.Stations)
                if (Array.IndexOf(obj.Stations, st) >= 0) { n++; break; }
        return n;
    }

    public RecipeView View(CompiledRecipe r)
    {
        lock (_gate)
        {
            if (_views.TryGetValue(r, out var v)) return v;
            var ings = new IngredientView[r.Inputs.Length];
            for (int i = 0; i < ings.Length; i++)
            {
                var ing = r.Inputs[i];
                var accepts = ing.Accepts.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
                if (ing.Group != null && _content.RecipeGroups.TryGet(ing.Group, out var group))
                {
                    var icon = group.Items.Length > 0 && _content.Items.TryGet(group.Items[0], out var first) ? first : accepts[0];
                    ings[i] = new IngredientView(group.DisplayName, icon, ing.Count, accepts);
                }
                else ings[i] = new IngredientView(accepts[0].DisplayName, accepts[0], ing.Count, accepts);
            }
            var stations = r.Def.Stations.Select(s => StationNames.Describe(s, _content)).ToArray();
            v = new RecipeView(r, r.Output, r.Def.Count, ings, stations, ConditionDescriber.Describe(r.Def.Conditions, _content));
            _views[r] = v;
            return v;
        }
    }

    private RecipeView[] Views(IReadOnlyList<CompiledRecipe> recipes)
    {
        var list = new RecipeView[recipes.Count];
        for (int i = 0; i < list.Length; i++) list[i] = View(recipes[i]);
        return list;
    }
}
