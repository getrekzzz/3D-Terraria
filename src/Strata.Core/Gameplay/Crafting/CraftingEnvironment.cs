using Strata.Core.Conditions;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>What a player can craft with right now: stations in reach and the surrounding conditions (liquids, biome, time).</summary>
public sealed class CraftingEnvironment
{
    private CraftingEnvironment(HashSet<string> stations, ConditionContext context)
    {
        Stations = stations;
        Context = context;
    }

    public HashSet<string> Stations { get; }
    public ConditionContext Context { get; }

    public static CraftingEnvironment Around(GameWorld world, PlayerEntity player)
    {
        var cell = player.FeetCell;
        var vw = world.Session.World;
        var context = world.Session.ContextAt(cell);
        context.NearbyLiquids = StationScanner.Liquids(vw, cell);
        return new CraftingEnvironment(StationScanner.Stations(vw, cell), context);
    }

    /// <summary>A recipe as shown in the crafting list.</summary>
    public sealed record Entry(CompiledRecipe Recipe, bool CanCraft, int MaxCrafts);

    /// <summary>
    /// Recipes available here (stations and conditions met), craftable first; each with how many times it could be crafted.
    /// </summary>
    public List<Entry> Browse(RecipeBook book, Items.Inventory inventory)
    {
        var list = new List<Entry>();
        foreach (var r in book.All)
        {
            if (!book.StationsAndConditionsMet(r, Stations, Context)) continue;
            int max = int.MaxValue;
            foreach (var ing in r.Inputs) max = Math.Min(max, inventory.Count(d => ing.Accepts.Contains(d)) / ing.Count);
            // Only list recipes whose ingredients the player has seen at least one of (keeps the list relevant).
            if (r.Inputs.All(ing => inventory.Count(d => ing.Accepts.Contains(d)) == 0)) continue;
            list.Add(new Entry(r, max > 0, max == int.MaxValue ? 0 : max));
        }
        return list.OrderByDescending(e => e.CanCraft).ThenBy(e => e.Recipe.Output.DisplayName, StringComparer.Ordinal).ToList();
    }
}
