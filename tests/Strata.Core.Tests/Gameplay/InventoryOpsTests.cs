using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class InventoryOpsTests
{
    private static ItemStack S(string id, int count = 1) => new(TestPaths.Content.Items.Get(id), count);

    private static int Count(Inventory inv, string id) => inv.Count(TestPaths.Content.Items.Get(id));

    private static PlayerInventory Empty()
    {
        var inv = new PlayerInventory { Content = TestPaths.Content };
        return inv;
    }

    [Fact]
    public void SortMergesStacksAndKeepsFavoritesInPlace()
    {
        var inv = Empty();
        var bp = inv.Backpack;
        bp[0] = S("gel", 3); // hotbar: left alone
        bp[12] = S("wood", 40);
        bp[13] = S("lesser_healing_potion", 2);
        bp[20] = S("wood", 30);
        bp[25] = S("copper_shortsword");
        bp[30] = S("torch", 5);
        bp[31] = S("gel", 7);
        bp[31 + 1] = S("copper_pickaxe");
        inv.Backpack.SetFavorite(30, true);

        InventoryOps.Sort(bp, PlayerInventory.HotbarSize, PlayerInventory.BackpackSize);

        Assert.Equal(S("gel", 3), bp[0]);
        Assert.Equal(S("torch", 5), bp[30]);
        Assert.True(bp.IsFavorite(30));
        // Weapons, tools, potions, placeables/materials…: the sword leads, the pickaxe follows.
        Assert.Equal("copper_shortsword", bp[10].Item!.Id);
        Assert.Equal("copper_pickaxe", bp[11].Item!.Id);
        Assert.Equal("lesser_healing_potion", bp[12].Item!.Id);
        Assert.Equal(70, Count(bp, "wood"));
        Assert.Single(Enumerable.Range(10, 40), i => bp[i].Item?.Id == "wood"); // merged into one stack
        Assert.Equal(10, Count(bp, "gel"));
        // Sword, pickaxe, potion, wood and gel fill slots 10–14; everything after is empty but the favourite.
        Assert.Equal(5, Enumerable.Range(10, 5).Count(i => !bp[i].IsEmpty));
        Assert.All(Enumerable.Range(15, 35).Where(i => i != 30), i => Assert.True(bp[i].IsEmpty));
    }

    [Fact]
    public void QuickStackOnlyMovesItemsAlreadyInChest()
    {
        var inv = Empty();
        var chest = new Inventory(new ItemStack[10]);
        chest[4] = S("wood", 9990);
        inv.Backpack[15] = S("wood", 50);
        inv.Backpack[16] = S("gel", 10);
        inv.Backpack[2] = S("wood", 5); // hotbar
        inv.Backpack[17] = S("wood", 7);
        inv.Backpack.SetFavorite(17, true);

        Assert.Equal(50, InventoryOps.QuickStack(inv, chest));
        Assert.Equal(9999, chest[4].Count); // topped up first, the rest in a free slot
        Assert.Equal(41, chest[0].Count);
        Assert.True(inv.Backpack[15].IsEmpty);
        Assert.Equal(10, inv.Backpack[16].Count);
        Assert.Equal(5, inv.Backpack[2].Count);
        Assert.Equal(7, inv.Backpack[17].Count);
        Assert.Equal(0, Count(chest, "gel"));
    }

    [Fact]
    public void RestockTopsUpBackpackStacks()
    {
        var inv = Empty();
        var chest = new Inventory(new ItemStack[10]);
        chest[0] = S("lesser_healing_potion", 20);
        chest[1] = S("wooden_arrow", 100);
        chest[2] = S("copper_shortsword");
        chest[3] = S("gel", 50);
        inv.Backpack[3] = S("lesser_healing_potion", 25); // stacks to 30
        inv.Ammo[0] = S("wooden_arrow", 9950);
        inv.Backpack[4] = S("copper_shortsword");

        Assert.Equal(5 + 49, InventoryOps.Restock(chest, inv));
        Assert.Equal(30, inv.Backpack[3].Count);
        Assert.Equal(9999, inv.Ammo[0].Count);
        Assert.Equal(15, chest[0].Count);
        Assert.Equal(51, chest[1].Count);
        Assert.Equal(1, Count(chest, "copper_shortsword")); // unstackable items stay
        Assert.Equal(50, Count(chest, "gel")); // and nothing the player does not already carry
    }

    [Fact]
    public void DepositAllSkipsHotbarCoinsAndFavorites()
    {
        var inv = Empty();
        var chest = new Inventory(new ItemStack[40]);
        inv.Backpack[0] = S("copper_pickaxe");
        inv.Backpack[12] = S("wood", 30);
        inv.Backpack[13] = S("gold_coin", 3);
        inv.Backpack[14] = S("gel", 9);
        inv.Backpack[15] = S("copper_shortsword");
        inv.Backpack.SetFavorite(15, true);

        Assert.Equal(39, InventoryOps.DepositAll(inv, chest));
        Assert.Equal(30, Count(chest, "wood"));
        Assert.Equal(9, Count(chest, "gel"));
        Assert.Equal("copper_pickaxe", inv.Backpack[0].Item!.Id);
        Assert.Equal(3, inv.Backpack[13].Count);
        Assert.Equal("copper_shortsword", inv.Backpack[15].Item!.Id);
        Assert.True(inv.Backpack.IsFavorite(15));
    }

    [Fact]
    public void LootAllRoutesCoinsToCoinSlots()
    {
        var inv = Empty();
        var chest = new Inventory(new ItemStack[10]);
        chest[0] = S("copper_coin", 150);
        chest[1] = S("wooden_arrow", 20);
        chest[2] = S("wood", 12);
        Assert.Equal(182, InventoryOps.LootAll(chest, inv));
        Assert.All(Enumerable.Range(0, chest.Size), i => Assert.True(chest[i].IsEmpty));
        Assert.Equal(150, Coins.Total(inv.Coins)); // and converted: 1 silver 50 copper
        Assert.Equal(1, Count(inv.Coins, "silver_coin"));
        Assert.Equal(20, Count(inv.Ammo, "wooden_arrow"));
        Assert.Equal(12, Count(inv.Backpack, "wood"));
    }

    [Fact]
    public void QuickStackNearbyReachesChestsInRangeOnly()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        inv.Backpack.Clear();
        var store = g.World.Session.World.TileObjects;
        var def = g.Content.TileObjects.Get("wooden_chest");
        var near = new TileObjectInstance(def, g.Floor + new Int3(9, 1, 0), 0, 0); // about 5 m away
        var far = new TileObjectInstance(def, g.Floor + new Int3(60, 1, 0), 0, 0); // 30 m away
        var locked = new TileObjectInstance(def, g.Floor + new Int3(-9, 1, 0), 0, 0) { Locked = true };
        foreach (var c in new[] { near, far, locked })
        {
            Assert.True(store.TryAdd(c));
            c.Container![0] = S("wood", 1);
        }
        near.Container![1] = S("gel", 1);
        inv.Backpack[20] = S("wood", 40);
        inv.Backpack[21] = S("gel", 6);
        inv.Backpack[22] = S("torch", 3);

        var reached = g.World.QuickStackNearby(g.Player);
        var (chest, moved) = Assert.Single(reached);
        Assert.Same(near, chest);
        Assert.Equal(46, moved);
        Assert.Equal(41, Count(new Inventory(near.Container!), "wood"));
        Assert.Equal(1, far.Container![0].Count);
        Assert.Equal(1, locked.Container![0].Count);
        Assert.Equal(3, Count(inv.Backpack, "torch"));
        Assert.True(g.World.Session.World.GetChunk(ChunkMath.CellToChunk(near.Anchor))!.Modified);
        Assert.Empty(g.World.QuickStackNearby(g.Player)); // nothing left that the chests hold
    }

    [Fact]
    public void ChestNameSurvivesWorldSave()
    {
        var dir = TestWorlds.TempDir();
        var anchor = Int3.Zero;
        using (var g = TestGame.Create(dir))
        {
            g.World.Spawner.Enabled = false;
            anchor = g.Floor + new Int3(16, 1, 0);
            var chest = new TileObjectInstance(g.Content.TileObjects.Get("wooden_chest"), anchor, 0, 0);
            Assert.True(g.World.Session.World.TileObjects.TryAdd(chest));
            Assert.True(g.World.RenameContainer(chest, "  Ore\tstash, the big one  "));
            Assert.Equal("Orestash, the big on", chest.CustomName);
            g.World.SaveAll();
        }
        var session = WorldSession.Load(TestPaths.Content, dir, new Strata.Core.Meshing.RenderTables(TestPaths.Content, _ => 0), new Strata.Core.Diagnostics.MemoryLog());
        session.Install(session.Produce(ChunkMath.CellToChunk(anchor)));
        var loaded = session.World.TileObjects.At(anchor);
        Assert.NotNull(loaded);
        Assert.Equal("Orestash, the big on", loaded!.CustomName);
        Assert.Equal("Orestash, the big on", loaded.DisplayName);
        loaded.CustomName = "   ";
        Assert.Null(loaded.CustomName);
        Assert.Equal(loaded.Def.DisplayName, loaded.DisplayName);
    }
}
