using Strata.Core.Conditions;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class InventoryCraftingTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = TestPaths.Content;
    private static ItemStack S(string id, int n) => new(Db.Items.Get(id), n);

    [Fact]
    public void AddMergesThenFillsEmptySlotsAndReportsRemainder()
    {
        var inv = new Inventory(3);
        Assert.True(inv.Add(S("torch", 50)).IsEmpty);
        Assert.True(inv.Add(S("torch", 30)).IsEmpty);
        Assert.Equal(80, inv[0].Count);
        Assert.True(inv.Add(S("copper_pickaxe", 1)).IsEmpty);
        Assert.True(inv.Add(S("copper_axe", 1)).IsEmpty);
        var rest = inv.Add(S("copper_hammer", 1));
        Assert.Equal(1, rest.Count);
        Assert.False(inv.CanFit(S("wood", 1)));
    }

    [Fact]
    public void RemoveAndTransferWork()
    {
        var a = new Inventory(2);
        var b = new Inventory(2);
        a.Add(S("wood", 10));
        b[1] = S("wood", 9990);
        Inventory.Transfer(a, 0, b, 1); // merges up to max stack
        Assert.Equal(9999, b[1].Count);
        Assert.Equal(1, a[0].Count);
        Assert.Equal(1, a.Remove(Db.Items.Get("wood"), 5));
        Assert.True(a[0].IsEmpty);
    }

    [Fact]
    public void CraftingNeedsStationsGroupsAndSpace()
    {
        var book = new RecipeBook(Db);
        var ctx = new ConditionContext();
        var inv = new Inventory(4);
        inv.Add(S("boreal_wood", 12));
        var workbench = book.RecipesFor(Db.Items.Get("work_bench"))[0];
        Assert.True(book.Craft(workbench, inv, new HashSet<string>(), ctx)); // any_wood group, no station needed
        Assert.Equal(1, inv.Count(Db.Items.Get("work_bench")));
        Assert.Equal(2, inv.Count(Db.Items.Get("boreal_wood")));

        var sword = book.RecipesFor(Db.Items.Get("wooden_sword"))[0];
        inv.Add(S("wood", 7));
        Assert.False(book.Craft(sword, inv, new HashSet<string>(), ctx)); // needs a workbench nearby
        Assert.True(book.Craft(sword, inv, new HashSet<string> { "work_bench" }, ctx));
        Assert.Equal(0, inv.Count(Db.Items.Get("wood")));
    }

    [Fact]
    public void CraftingFailsAtomicallyWhenOutputCannotFit()
    {
        var book = new RecipeBook(Db);
        var inv = new Inventory(2);
        inv[0] = S("wood", 7);
        inv[1] = S("copper_axe", 1);
        var sword = book.RecipesFor(Db.Items.Get("wooden_sword"))[0];
        // Consuming all 7 wood frees slot 0, so this fits; now block it with a non-stackable item.
        inv[0] = S("wood", 8);
        var trial = book.Craft(sword, inv, new HashSet<string> { "work_bench" }, new ConditionContext());
        Assert.False(trial);
        Assert.Equal(8, inv.Count(Db.Items.Get("wood")));
    }

    [Fact]
    public void EnvironmentConditionsGateRecipes()
    {
        var book = new RecipeBook(Db);
        var recipe = book.RecipesFor(Db.Items.Get("bottled_water"))[0]; // a bottle filled at the water's edge
        var stations = new HashSet<string>();
        Assert.False(book.StationsAndConditionsMet(recipe, stations, new ConditionContext()));
        Assert.True(book.StationsAndConditionsMet(recipe, stations, new ConditionContext { NearbyLiquids = new HashSet<string> { "water" } }));
    }

    [Fact]
    public void LootRollsAreDeterministicAndIncludeCoins()
    {
        var roller = new LootRoller(Db);
        var r1 = new Pcg32(5);
        var r2 = new Pcg32(5);
        var a = roller.Roll("cabin_chest", ref r1);
        var b = roller.Roll("cabin_chest", ref r2);
        Assert.Equal(a, b);
        Assert.Contains(a, s => s.Item!.HasTag("coin"));
        // One treasure from the chest's primary pool.
        var primary = Db.LootTables.Get("cabin_chest_primary").Entries.Select(e => e.Item).ToHashSet();
        Assert.Contains(a, s => primary.Contains(s.Item!.Id));
    }

    [Fact]
    public void CoinsConvertBothWays()
    {
        var stacks = Coins.ToStacks(Db, 1_234_567);
        Assert.Equal(new[] { 1, 23, 45, 67 }, stacks.Select(s => s.Count));
        var inv = new Inventory(8);
        foreach (var s in stacks) inv.Add(s);
        Assert.Equal(1_234_567, Coins.Total(inv));
    }
}
