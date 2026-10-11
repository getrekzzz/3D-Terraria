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
    public void BoneWelderCraftsBoneBlocks()
    {
        var book = new RecipeBook(Db);
        var inv = new Inventory(4);
        inv.Add(S("bone", 3));
        var recipe = book.RecipesFor(Db.Items.Get("bone_block"))[0];
        Assert.False(book.Craft(recipe, inv, new HashSet<string> { "work_bench" }, new ConditionContext()));
        Assert.True(book.Craft(recipe, inv, new HashSet<string> { "bone_welder" }, new ConditionContext()));
        Assert.Equal(1, inv.Count(Db.Items.Get("bone_block")));
        Assert.Equal(2, inv.Count(Db.Items.Get("bone")));
        Assert.Contains("bone_welder", Db.TileObjects.Get("bone_welder").Stations);
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
    public void CraftIntoCursorMergesSameItem()
    {
        var book = new RecipeBook(Db);
        var torch = book.RecipesFor(Db.Items.Get("torch"))[0]; // 1 gel + 1 any wood → 3 torches
        var inv = new PlayerInventory();
        inv.Backpack.Add(S("gel", 3));
        inv.Backpack.Add(S("boreal_wood", 3));
        var cursor = ItemStack.Empty;
        var none = new HashSet<string>();
        Assert.True(book.CraftInto(ref cursor, torch, inv, none, new ConditionContext()));
        Assert.Equal(S("torch", 3), cursor);
        Assert.True(book.CraftInto(ref cursor, torch, inv, none, new ConditionContext()));
        Assert.Equal(6, cursor.Count);
        Assert.Equal(1, inv.CountAll(Db.Items.Get("gel")));
        Assert.Equal(1, inv.CountAll(Db.Items.Get("boreal_wood")));
        Assert.Equal(0, inv.Backpack.Count(Db.Items.Get("torch"))); // onto the cursor, not into the backpack

        // A plain inventory crafts onto the cursor the same way; the last craft runs out of gel.
        var chest = new Inventory(4);
        chest.Add(S("gel", 1));
        chest.Add(S("wood", 5));
        Assert.True(book.CraftInto(ref cursor, torch, chest, none, new ConditionContext()));
        Assert.Equal(9, cursor.Count);
        Assert.False(book.CraftInto(ref cursor, torch, chest, none, new ConditionContext()));
        Assert.Equal(9, cursor.Count);
        Assert.Equal(4, chest.Count(Db.Items.Get("wood")));
    }

    [Fact]
    public void CraftIntoRefusesDifferentCursorItemAndFullStack()
    {
        var book = new RecipeBook(Db);
        var torch = book.RecipesFor(Db.Items.Get("torch"))[0];
        var sword = book.RecipesFor(Db.Items.Get("wooden_sword"))[0];
        var inv = new PlayerInventory();
        inv.Backpack.Add(S("gel", 5));
        inv.Backpack.Add(S("wood", 20));
        var none = new HashSet<string>();
        var bench = new HashSet<string> { "work_bench" };

        var cursor = S("dirt", 4); // another item in hand
        Assert.False(book.CraftInto(ref cursor, torch, inv, none, new ConditionContext()));
        Assert.Equal(S("dirt", 4), cursor);

        cursor = S("torch", Db.Items.Get("torch").MaxStack - 2); // no room for all 3
        Assert.False(book.CraftInto(ref cursor, torch, inv, none, new ConditionContext()));
        Assert.Equal(Db.Items.Get("torch").MaxStack - 2, cursor.Count);

        // A weapon never stacks: a sword in hand refuses a second, and an empty hand takes it.
        cursor = ItemStack.Empty;
        Assert.True(book.CraftInto(ref cursor, sword, inv, bench, new ConditionContext()));
        Assert.Equal("wooden_sword", cursor.Item!.Id);
        var held = cursor;
        Assert.False(book.CraftInto(ref cursor, sword, inv, bench, new ConditionContext()));
        Assert.Equal(held, cursor);
        Assert.False(book.CraftInto(ref cursor, sword, inv, none, new ConditionContext())); // and no bench, no sword
        Assert.Equal(5, inv.CountAll(Db.Items.Get("gel")));
        Assert.Equal(13, inv.CountAll(Db.Items.Get("wood")));
    }

    [Fact]
    public void SignatureStableAcrossIdenticalBrowses()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        inv.Backpack.Clear();
        inv.Pickup(S("wood", 30));
        inv.Pickup(S("gel", 2));
        var book = new RecipeBook(g.Content);
        var env = CraftingEnvironment.Around(g.World, g.Player);
        var first = env.Browse(book, inv);
        ulong signature = CraftingEnvironment.Signature(first);
        Assert.Equal(signature, CraftingEnvironment.Signature(env.Browse(book, inv)));
        Assert.Equal(signature, CraftingEnvironment.Signature(CraftingEnvironment.Around(g.World, g.Player).Browse(book, inv)));
        // Moving items between slots changes nothing a list shows; using some changes how many crafts are left.
        inv.Backpack[49] = inv.Backpack[0];
        inv.Backpack[0] = ItemStack.Empty;
        Assert.Equal(signature, CraftingEnvironment.Signature(env.Browse(book, inv)));
        inv.Backpack.Remove(Db.Items.Get("gel"), 1);
        Assert.NotEqual(signature, CraftingEnvironment.Signature(env.Browse(book, inv)));
        Assert.Equal(CraftingEnvironment.Signature(new()), CraftingEnvironment.Signature(new()));
    }

    [Fact]
    public void RecipeTextNamesGroupsAndConditions()
    {
        var book = new RecipeBook(Db);
        var torch = book.RecipesFor(Db.Items.Get("torch"))[0];
        Assert.Contains(torch.Inputs, i => RecipeText.Ingredient(i, Db) == "Any Wood");
        Assert.Contains(torch.Inputs, i => RecipeText.Ingredient(i, Db) == Db.Items.Get("gel").DisplayName);
        Assert.Equal("", RecipeText.Conditions(torch.Def, Db));
        Assert.Equal("Near Water", RecipeText.Conditions(book.RecipesFor(Db.Items.Get("bottled_water"))[0].Def, Db));
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
