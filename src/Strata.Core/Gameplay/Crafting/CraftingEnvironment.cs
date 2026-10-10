using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>What a player can craft with right now: stations in reach and the surrounding conditions (liquids, biome, time).</summary>
public sealed class CraftingEnvironment
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private CraftingEnvironment(HashSet<string> stations, List<string> objectNames, ConditionContext context)
    {
        Stations = stations;
        ObjectNames = objectNames;
        Context = context;
    }

    public HashSet<string> Stations { get; }

    /// <summary>The display names of the station objects in reach, each once, sorted ("Hellforge", "Iron Anvil").</summary>
    public IReadOnlyList<string> ObjectNames { get; }

    public ConditionContext Context { get; }

    public static CraftingEnvironment Around(GameWorld world, PlayerEntity player)
    {
        var cell = player.FeetCell;
        var vw = world.Session.World;
        var context = world.Session.ContextAt(cell);
        context.NearbyLiquids = StationScanner.Liquids(vw, cell);
        var names = new List<string>();
        var stations = StationScanner.Stations(vw, cell, names);
        names.Sort(StringComparer.Ordinal);
        return new CraftingEnvironment(stations, names, context);
    }

    /// <summary>A recipe as shown in the crafting list.</summary>
    public sealed record Entry(CompiledRecipe Recipe, bool CanCraft, int MaxCrafts);

    /// <summary>
    /// Recipes available here (stations and conditions met), craftable first; each with how many times it could be crafted.
    /// </summary>
    public List<Entry> Browse(RecipeBook book, Inventory inventory) => Browse(book, Tally(inventory));

    /// <summary>The same over a player's backpack, coin and ammo slots (arrows in the ammo slots craft too).</summary>
    public List<Entry> Browse(RecipeBook book, PlayerInventory inventory) => Browse(book, Tally(inventory.Backpack, inventory.Coins, inventory.Ammo));

    /// <summary>
    /// A stable hash of a browse (each entry's recipe id, whether it can be crafted and how many times), so a list showing
    /// it rebuilds only when it changed. FNV-1a over the ids' characters: equal browses hash alike in every run.
    /// </summary>
    public static ulong Signature(List<Entry> entries)
    {
        ulong h = FnvOffset;
        foreach (var e in entries)
        {
            foreach (char c in e.Recipe.Def.Id) h = (h ^ c) * FnvPrime;
            h = (h ^ (e.CanCraft ? 0x10000UL : 0x20000UL)) * FnvPrime; // above any id character: also ends the id
            h = (h ^ (uint)e.MaxCrafts) * FnvPrime;
        }
        return h;
    }

    /// <summary>How many of the items an ingredient accepts the counted storages hold.</summary>
    private static int Have(Ingredient ing, Dictionary<ItemDef, int> tally)
    {
        int n = 0;
        foreach (var item in ing.Accepts)
            if (tally.TryGetValue(item, out int c)) n += c;
        return n;
    }

    /// <summary>Every item in the storages with its total count: one pass, so a browse never rescans the slots per recipe.</summary>
    private static Dictionary<ItemDef, int> Tally(params Inventory[] storages)
    {
        var tally = new Dictionary<ItemDef, int>();
        foreach (var storage in storages)
            for (int i = 0; i < storage.Size; i++)
            {
                var s = storage[i];
                if (!s.IsEmpty) tally[s.Item!] = tally.GetValueOrDefault(s.Item!) + s.Count;
            }
        return tally;
    }

    private List<Entry> Browse(RecipeBook book, Dictionary<ItemDef, int> tally)
    {
        var list = new List<Entry>();
        foreach (var r in book.All)
        {
            if (!book.StationsAndConditionsMet(r, Stations, Context)) continue;
            int max = int.MaxValue;
            bool seen = false; // only recipes with an ingredient the player has at least one of (keeps the list relevant)
            foreach (var ing in r.Inputs)
            {
                int have = Have(ing, tally);
                seen |= have > 0;
                max = Math.Min(max, have / ing.Count);
            }
            if (!seen) continue;
            list.Add(new Entry(r, max > 0, max == int.MaxValue ? 0 : max));
        }
        return list.OrderByDescending(e => e.CanCraft).ThenBy(e => e.Recipe.Output.DisplayName, StringComparer.Ordinal).ToList();
    }
}
