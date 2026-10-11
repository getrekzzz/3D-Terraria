using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class SlotClickTests
{
    private static ItemStack S(string id, int count = 1) => new(TestPaths.Content.Items.Get(id), count);

    private static ClickOutcome Click(TestGame g, SlotArea area, int index, bool right = false, ClickMods mods = ClickMods.None, SlotContext ctx = default) =>
        g.World.ClickSlot(g.Player, new SlotRef(area, index), right, mods, ctx);

    private static PlayerInventory Fresh(TestGame g)
    {
        var inv = g.Player.State.Inventory;
        inv.Backpack.Clear();
        inv.Coins.Clear();
        inv.Ammo.Clear();
        return inv;
    }

    [Fact]
    public void RightClickTakesOneAtATime()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Backpack[12] = S("torch", 9);
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Backpack, 12, right: true));
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Backpack, 12, right: true));
        Assert.Equal(S("torch", 2), inv.Cursor);
        Assert.Equal(7, inv.Backpack[12].Count);

        // A different item on the cursor blocks taking; the same item keeps stacking up.
        inv.Backpack[13] = S("gel", 5);
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Backpack, 13, right: true));
        Assert.Equal(5, inv.Backpack[13].Count);
        for (int i = 0; i < 7; i++) Click(g, SlotArea.Backpack, 12, right: true);
        Assert.Equal(S("torch", 9), inv.Cursor);
        Assert.True(inv.Backpack[12].IsEmpty);
        // The old Minecraft habit (right click drops one into a slot) is gone: nothing is placed.
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Backpack, 14, right: true));
        Assert.True(inv.Backpack[14].IsEmpty);
    }

    [Fact]
    public void RightClickEquipsArmorAndSwapsOld()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Backpack[15] = S("copper_helmet");
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Backpack, 15, right: true));
        Assert.Equal("copper_helmet", inv.Armor[0].Item!.Id);
        Assert.True(inv.Backpack[15].IsEmpty);

        inv.Backpack[16] = S("iron_helmet");
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Backpack, 16, right: true));
        Assert.Equal("iron_helmet", inv.Armor[0].Item!.Id);
        Assert.Equal("copper_helmet", inv.Backpack[16].Item!.Id); // the old piece takes the clicked slot
        Assert.True(inv.Cursor.IsEmpty);
        Assert.Equal(g.Content.Items.Get("iron_helmet").Defense, g.Player.State.Defense);
    }

    [Fact]
    public void RightClickEquippedArmorSwapsWithVanity()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Armor[0] = S("iron_helmet");
        inv.VanityArmor[0] = S("copper_helmet");
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Armor, 0, right: true));
        Assert.Equal("copper_helmet", inv.Armor[0].Item!.Id);
        Assert.Equal("iron_helmet", inv.VanityArmor[0].Item!.Id);
        // From the vanity side too, and with one side empty the piece just moves across.
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.VanityArmor, 0, right: true));
        Assert.Equal("iron_helmet", inv.Armor[0].Item!.Id);
        inv.VanityArmor[0] = ItemStack.Empty;
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Armor, 0, right: true));
        Assert.True(inv.Armor[0].IsEmpty);
        Assert.Equal("iron_helmet", inv.VanityArmor[0].Item!.Id);

        // Pure vanity never moves into the worn row.
        var cape = new ItemDef { Id = "test_vanity_cape", Name = "Vanity Cape", Category = ItemCategory.Vanity, EquipSlot = EquipSlot.Accessory };
        inv.VanityAccessories[1] = new ItemStack(cape, 1);
        inv.Accessories[1] = S("hermes_boots");
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Accessories, 1, right: true));
        Assert.Equal("hermes_boots", inv.Accessories[1].Item!.Id);
    }

    [Fact]
    public void RightClickAccessoryWhenFullRotates()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        var ids = new[] { "hermes_boots", "cloud_in_a_bottle", "band_of_regeneration", "aglet", "anklet_of_the_wind" };
        for (int i = 0; i < ids.Length; i++)
        {
            inv.Backpack[20 + i] = S(ids[i]);
            Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Backpack, 20 + i, right: true));
            Assert.Equal(ids[i], inv.Accessories[i].Item!.Id);
        }
        // All five unlocked slots are full: the next ones replace slot 0, then slot 1.
        inv.Backpack[30] = S("shiny_red_balloon");
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Backpack, 30, right: true));
        Assert.Equal("shiny_red_balloon", inv.Accessories[0].Item!.Id);
        Assert.Equal("hermes_boots", inv.Backpack[30].Item!.Id);
        inv.Backpack[31] = S("lucky_horseshoe");
        Click(g, SlotArea.Backpack, 31, right: true);
        Assert.Equal("lucky_horseshoe", inv.Accessories[1].Item!.Id);
        Assert.True(inv.Accessories[5].IsEmpty); // the locked 6th slot is never used

        // A copy of a worn accessory is refused and stays put.
        inv.Backpack[32] = S("aglet");
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Backpack, 32, right: true));
        Assert.Equal("aglet", inv.Backpack[32].Item!.Id);
    }

    [Fact]
    public void RightClickOpensWoodenCrate()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Backpack[11] = S("wooden_crate", 3);
        int opened = 0;
        g.World.ItemUse.Opened += (_, item) => opened += item.Id == "wooden_crate" ? 1 : 0;
        Assert.Equal(ClickOutcome.Opened, Click(g, SlotArea.Backpack, 11, right: true));
        Assert.Equal(2, inv.Backpack[11].Count);
        Assert.Equal(1, opened);
        Assert.True(inv.Cursor.IsEmpty);
        Assert.True(Coins.Total(inv.Coins) >= 1000); // the crate's coins land in the coin slots
        Assert.True(inv.Backpack.Stacks.Count() > 1); // and its loot in the backpack
    }

    [Fact]
    public void CtrlClickTrashesAndReplacesOldTrash()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Backpack[3] = S("wood", 20);
        inv.Backpack[4] = S("gel", 7);
        Assert.Equal(ClickOutcome.Trashed, Click(g, SlotArea.Backpack, 3, mods: ClickMods.Ctrl));
        Assert.Equal(S("wood", 20), inv.Trash[0]);
        Assert.True(inv.Backpack[3].IsEmpty);
        Assert.Equal(ClickOutcome.Trashed, Click(g, SlotArea.Backpack, 4, mods: ClickMods.Ctrl));
        Assert.Equal(S("gel", 7), inv.Trash[0]); // the wood is gone for good
        Assert.Equal(0, inv.CountAll(g.Content.Items.Get("wood")));
        // Shift-clicking the trash takes the item back.
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Trash, 0, mods: ClickMods.Shift));
        Assert.Equal(7, inv.CountAll(g.Content.Items.Get("gel")));
    }

    [Fact]
    public void AltClickFavoritesAndTrashRefusesFavorites()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Backpack[5] = S("torch", 10);
        Assert.Equal(ClickOutcome.Favorited, Click(g, SlotArea.Backpack, 5, mods: ClickMods.Alt));
        Assert.True(inv.Backpack.IsFavorite(5));
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Backpack, 5, mods: ClickMods.Ctrl));
        Assert.Equal(10, inv.Backpack[5].Count);
        Assert.True(inv.Trash[0].IsEmpty);
        Assert.Equal(ClickOutcome.Favorited, Click(g, SlotArea.Backpack, 5, mods: ClickMods.Alt));
        Assert.False(inv.Backpack.IsFavorite(5));
        Assert.Equal(ClickOutcome.Trashed, Click(g, SlotArea.Backpack, 5, mods: ClickMods.Ctrl));
        // Worn equipment keeps no marks.
        inv.Armor[0] = S("iron_helmet");
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Armor, 0, mods: ClickMods.Alt));
    }

    [Fact]
    public void ShiftClickMovesToOpenContainer()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        var chest = new TileObjectInstance(g.Content.TileObjects.Get("wooden_chest"), g.Floor + new Int3(3, 1, 0), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(chest));
        var ctx = SlotContext.Of(chest);
        inv.Backpack[12] = S("wood", 30);
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Backpack, 12, mods: ClickMods.Shift, ctx: ctx));
        Assert.True(inv.Backpack[12].IsEmpty);
        Assert.Equal(S("wood", 30), chest.Container![0]);
        Assert.True(g.World.Session.World.GetChunk(ChunkMath.CellToChunk(chest.Anchor))!.Modified);

        // And back, coins to the coin slots.
        chest.Container[1] = S("silver_coin", 5);
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Container, 1, mods: ClickMods.Shift, ctx: ctx));
        Assert.Equal(5, inv.Coins.Count(g.Content.Items.Get("silver_coin")));
        // Shift with nothing open equips from the backpack and unequips back to it.
        inv.Backpack[13] = S("copper_helmet");
        Assert.Equal(ClickOutcome.Equipped, Click(g, SlotArea.Backpack, 13, mods: ClickMods.Shift));
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Armor, 0, mods: ClickMods.Shift));
        Assert.True(inv.Armor[0].IsEmpty);
        Assert.Equal(1, inv.Backpack.Count(g.Content.Items.Get("copper_helmet")));
    }

    [Fact]
    public void LeftClickUsesTheSlotFilters()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        inv.Cursor = S("wood", 5);
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Coins, 0));
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Ammo, 0));
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Armor, 0));
        inv.Cursor = S("wooden_arrow", 50);
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Ammo, 2));
        Assert.Equal(50, inv.Ammo[2].Count);
        Assert.True(inv.Cursor.IsEmpty);
    }

    [Fact]
    public void ThrowCursorSpawnsADrop()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        int before = g.World.Drops.Items.Count;
        inv.Cursor = S("gel", 4);
        Assert.Equal(ClickOutcome.Thrown, g.World.ThrowCursor(g.Player, one: true));
        Assert.Equal(S("gel", 3), inv.Cursor);
        Assert.Equal(ClickOutcome.Thrown, g.World.ThrowCursor(g.Player, one: false));
        Assert.True(inv.Cursor.IsEmpty);
        Assert.Equal(before + 2, g.World.Drops.Items.Count);
        Assert.Equal(ClickOutcome.None, g.World.ThrowCursor(g.Player, one: false));
    }

    [Fact]
    public void HoldRepeatAccelerates()
    {
        Assert.Equal(30, HoldRepeat.IntervalTicks(0));
        Assert.Equal(22, HoldRepeat.IntervalTicks(1));
        Assert.Equal(17, HoldRepeat.IntervalTicks(2));
        int last = int.MaxValue;
        for (int i = 0; i < 40; i++)
        {
            int t = HoldRepeat.IntervalTicks(i);
            Assert.True(t <= last && t >= 1);
            last = t;
        }
        Assert.Equal(1, last);
    }

    [Fact]
    public void ShiftClickPutsAnItemInTheOpenReforgeSlot()
    {
        using var g = TestGame.Create();
        var inv = Fresh(g);
        var ctx = new SlotContext(Service: true);
        inv.Backpack[12] = S("wood", 10);
        Assert.Equal(ClickOutcome.None, Click(g, SlotArea.Backpack, 12, mods: ClickMods.Shift, ctx: ctx)); // no prefix: refused
        Assert.True(inv.Service[0].IsEmpty);
        inv.Backpack[13] = S("copper_broadsword");
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Backpack, 13, mods: ClickMods.Shift, ctx: ctx));
        Assert.Equal("copper_broadsword", inv.Service[0].Item!.Id);
        Assert.True(inv.Backpack[13].IsEmpty);
        // Another one swaps with what the slot held; Shift on the slot takes it back to the backpack.
        inv.Backpack[14] = S("wooden_sword");
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Backpack, 14, mods: ClickMods.Shift, ctx: ctx));
        Assert.Equal("wooden_sword", inv.Service[0].Item!.Id);
        Assert.Equal("copper_broadsword", inv.Backpack[14].Item!.Id);
        Assert.Equal(ClickOutcome.Moved, Click(g, SlotArea.Service, 0, mods: ClickMods.Shift, ctx: ctx));
        Assert.True(inv.Service[0].IsEmpty);
        Assert.Equal(1, inv.Backpack.Count(g.Content.Items.Get("wooden_sword")));
    }
}
