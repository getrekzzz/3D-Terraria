using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>
/// Indexed recipes with station/environment checks and atomic crafting (inputs are only consumed when the
/// output fits). Scales to thousands of recipes: lookups are by output and by ingredient.
/// </summary>
public sealed class RecipeBook
{
    private readonly List<CompiledRecipe> _recipes = new();
    private readonly Dictionary<ItemDef, List<CompiledRecipe>> _byIngredient = new();
    private readonly Dictionary<ItemDef, List<CompiledRecipe>> _byOutput = new();
    private readonly ContentDatabase _content;
    private Determinism.Pcg32 _rng = new(0x43524146UL);

    public RecipeBook(ContentDatabase content)
    {
        _content = content;
        foreach (var r in content.Recipes.All)
        {
            var inputs = r.Inputs.Select(i => new Ingredient(
                i.Item != null ? new HashSet<ItemDef> { content.Items.Get(i.Item) } : content.RecipeGroups.Get(i.Group!).Items.Select(content.Items.Get).ToHashSet(),
                i.Count, i.Group)).ToArray();
            var compiled = new CompiledRecipe(r, content.Items.Get(r.Output), inputs, ConditionCompiler.CompileAll(r.Conditions));
            _recipes.Add(compiled);
            Index(_byOutput, compiled.Output, compiled);
            foreach (var ing in inputs)
                foreach (var item in ing.Accepts) Index(_byIngredient, item, compiled);
        }
    }

    public IReadOnlyList<CompiledRecipe> All => _recipes;

    public IReadOnlyList<CompiledRecipe> RecipesFor(ItemDef output) => _byOutput.TryGetValue(output, out var l) ? l : Array.Empty<CompiledRecipe>();

    public IReadOnlyList<CompiledRecipe> RecipesUsing(ItemDef ingredient) => _byIngredient.TryGetValue(ingredient, out var l) ? l : Array.Empty<CompiledRecipe>();

    /// <summary>Recipes whose stations and conditions are satisfied and whose inputs the inventory holds.</summary>
    public IEnumerable<CompiledRecipe> Available(Inventory inventory, IReadOnlySet<string> stations, ConditionContext context) =>
        _recipes.Where(r => CanCraft(r, inventory, stations, context));

    public bool StationsAndConditionsMet(CompiledRecipe r, IReadOnlySet<string> stations, ConditionContext context) =>
        r.Def.Stations.All(stations.Contains) && r.Condition.Evaluate(context);

    public bool CanCraft(CompiledRecipe r, Inventory inventory, IReadOnlySet<string> stations, ConditionContext context) =>
        StationsAndConditionsMet(r, stations, context) && r.Inputs.All(i => inventory.Count(d => i.Accepts.Contains(d)) >= i.Count);

    /// <summary>Crafts once. Returns false (changing nothing) if requirements fail or the output does not fit.</summary>
    public bool Craft(CompiledRecipe r, Inventory inventory, IReadOnlySet<string> stations, ConditionContext context)
    {
        if (!CanCraft(r, inventory, stations, context)) return false;
        var output = Prefixes.Fresh(r.Output, r.Def.Count, _content, ref _rng);
        // Simulate: remove inputs from a copy, then check the output fits.
        var trial = new Inventory(inventory.ToArray());
        foreach (var ing in r.Inputs) trial.Remove(d => ing.Accepts.Contains(d), ing.Count);
        if (!trial.CanFit(output)) return false;
        foreach (var ing in r.Inputs) inventory.Remove(d => ing.Accepts.Contains(d), ing.Count);
        inventory.Add(output);
        return true;
    }

    private static void Index(Dictionary<ItemDef, List<CompiledRecipe>> index, ItemDef key, CompiledRecipe r)
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = new List<CompiledRecipe>();
        if (!list.Contains(r)) list.Add(r);
    }
}

public sealed record Ingredient(HashSet<ItemDef> Accepts, int Count, string? Group);

public sealed record CompiledRecipe(RecipeDef Def, ItemDef Output, Ingredient[] Inputs, ICondition Condition);
