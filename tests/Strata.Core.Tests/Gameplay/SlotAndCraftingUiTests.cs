using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class SlotAndCraftingUiTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = TestPaths.Content;
    private static ItemStack S(string id, int n) => new(Db.Items.Get(id), n);

    [Fact]
    public void LeftClickPicksUpPlacesMergesAndSwaps()
    {
        var inv = new Inventory(3);
        inv[0] = S("wood", 10);
        inv[1] = S("wood", 5);
        inv[2] = S("gel", 3);
        var cursor = ItemStack.Empty;
        SlotActions.LeftClick(ref cursor, inv, 0);
        Assert.Equal(10, cursor.Count);
        Assert.True(inv[0].IsEmpty);
        SlotActions.LeftClick(ref cursor, inv, 1); // merge
        Assert.True(cursor.IsEmpty);
        Assert.Equal(15, inv[1].Count);
        SlotActions.LeftClick(ref cursor, inv, 2); // pick gel
        SlotActions.LeftClick(ref cursor, inv, 1); // swap gel <-> wood
        Assert.Equal("gel", inv[1].Item!.Id);
        Assert.Equal("wood", cursor.Item!.Id);
    }

    [Fact]
    public void RightClickTakesOneInsteadOfSplitting()
    {
        var inv = new PlayerInventory();
        inv.Backpack[12] = S("torch", 9);
        Assert.Equal(ClickOutcome.Moved, SlotActions.RightClick(inv, new SlotRef(SlotArea.Backpack, 12), inv.Backpack));
        Assert.Equal(1, inv.Cursor.Count);
        Assert.Equal(8, inv.Backpack[12].Count);
        // An empty slot takes nothing from the cursor.
        Assert.Equal(ClickOutcome.None, SlotActions.RightClick(inv, new SlotRef(SlotArea.Backpack, 13), inv.Backpack));
        Assert.True(inv.Backpack[13].IsEmpty);
    }

    [Fact]
    public void EquipmentSlotsFilterItems()
    {
        var armor = new Inventory(3);
        var cursor = S("wood", 1);
        SlotActions.LeftClick(ref cursor, armor, 0, PlayerInventory.ArmorFilter(EquipSlot.Head));
        Assert.True(armor[0].IsEmpty);
        cursor = S("copper_helmet", 1);
        SlotActions.LeftClick(ref cursor, armor, 0, PlayerInventory.ArmorFilter(EquipSlot.Head));
        Assert.Equal("copper_helmet", armor[0].Item!.Id);

        var acc = new Inventory(2);
        acc[0] = S("hermes_boots", 1);
        cursor = S("hermes_boots", 1);
        SlotActions.LeftClick(ref cursor, acc, 1, PlayerInventory.AccessoryFilter(acc, 1));
        Assert.True(acc[1].IsEmpty); // no duplicate accessories
    }

    [Fact]
    public void CraftingEnvironmentSeesPlacedStations()
    {
        using var g = TestGame.Create();
        g.Player.State.Inventory.Backpack.Clear();
        g.Player.State.Inventory.Pickup(S("wood", 30));
        var book = new RecipeBook(g.Content);
        var env = CraftingEnvironment.Around(g.World, g.Player);
        var entries = env.Browse(book, g.Player.State.Inventory.Backpack);
        Assert.Contains(entries, e => e.Recipe.Output.Id == "work_bench" && e.CanCraft);
        Assert.DoesNotContain(entries, e => e.Recipe.Output.Id == "wooden_chair"); // needs a workbench

        Assert.Equal(Strata.Core.Gameplay.Building.PlacementResult.Ok,
            g.World.Editor.PlaceObject(g.Content.TileObjects.Get("work_bench"), g.Floor + new Strata.Core.Common.Int3(2, 1, 0), 0));
        env = CraftingEnvironment.Around(g.World, g.Player);
        Assert.Contains("work_bench", env.Stations);
        Assert.Equal(new[] { "Work Bench" }, env.ObjectNames);
        entries = env.Browse(book, g.Player.State.Inventory.Backpack);
        var chair = Assert.Single(entries, e => e.Recipe.Output.Id == "wooden_chair");
        Assert.True(chair.CanCraft);
        Assert.True(book.Craft(chair.Recipe, g.Player.State.Inventory.Backpack, env.Stations, env.Context));
        Assert.Equal(1, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("wooden_chair")));
    }

    [Fact]
    public void TooltipsDescribeWeaponsAndTools()
    {
        var uses = new ItemUseTable(Db);
        var pick = Db.Items.Get("copper_pickaxe");
        var lines = ItemTooltip.Lines(new ItemStack(pick, 1), uses[pick], usedInRecipes: false).Select(l => l.Text).ToList();
        Assert.Equal("Copper Pickaxe", lines[0]);
        Assert.Contains("35% pickaxe power", lines);
        Assert.Contains(lines, l => l.EndsWith("melee damage"));
        Assert.Contains(lines, l => l.StartsWith("Sell price"));
        Assert.Equal("Very fast", ItemTooltip.Speed(20));
    }
}
