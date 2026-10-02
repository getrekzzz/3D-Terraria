using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class PlayerInventoryTests
{
    private static ItemDef Item(string id) => TestPaths.Content.Items.Get(id);

    private static ItemStack S(string id, int count = 1) => new(Item(id), count);

    // No vanity items or dyes ship yet, so these stand in for them (never saved, so they never reach the content).
    private static readonly ItemDef VanityShirt = new() { Id = "test_vanity_shirt", Name = "Vanity Shirt", Category = ItemCategory.Vanity, EquipSlot = EquipSlot.Body, Defense = 9 };
    private static readonly ItemDef VanityCape = new() { Id = "test_vanity_cape", Name = "Vanity Cape", Category = ItemCategory.Vanity, EquipSlot = EquipSlot.Accessory };
    private static readonly ItemDef RedDye = new() { Id = "test_red_dye", Name = "Red Dye", Category = ItemCategory.Dye, MaxStack = 9999 };

    [Fact]
    public void EquippedStacksIgnoreVanityDyesMiscAndLockedAccessorySlots()
    {
        var p = new PlayerState("tester");
        var inv = p.Inventory;
        inv.Armor[0] = S("copper_helmet");
        inv.VanityArmor[2] = S("wood_greaves");
        inv.VanityArmor[1] = new ItemStack(VanityShirt, 1);
        inv.Accessories[0] = S("cobalt_shield");
        inv.Accessories[5] = S("obsidian_skull"); // the Expert slot, locked in a Classic world
        inv.VanityAccessories[0] = S("obsidian_shield");
        inv.Dyes[0] = new ItemStack(RedDye, 3);
        inv.Misc[(int)MiscSlot.Hook] = S("grappling_hook");

        var worn = inv.EquippedStacks.Select(s => s.Item!.Id).ToList();
        Assert.Equal(new[] { "copper_helmet", "cobalt_shield" }, worn);
        // Equipping recomputes on its own: only the helmet and the shield count.
        Assert.Equal(Item("copper_helmet").Defense + Item("cobalt_shield").Defense, p.Defense);

        inv.AccessorySlotsUnlocked = 6;
        Assert.Contains(inv.EquippedStacks, s => s.Item!.Id == "obsidian_skull");
        Assert.Equal(Item("copper_helmet").Defense + Item("cobalt_shield").Defense + Item("obsidian_skull").Defense, p.Defense);
        inv.AccessorySlotsUnlocked = 99;
        Assert.Equal(PlayerInventory.MaxAccessorySlots, inv.AccessorySlotsUnlocked);
        inv.Armor[0] = ItemStack.Empty;
        Assert.Equal(Item("cobalt_shield").Defense + Item("obsidian_skull").Defense, p.Defense);
    }

    [Fact]
    public void ShownEquipmentPrefersVanityAndSkipsHiddenOrLockedAccessories()
    {
        var inv = new PlayerState("tester").Inventory;
        inv.Armor[0] = S("copper_helmet");
        inv.Armor[1] = S("copper_chainmail");
        inv.VanityArmor[1] = new ItemStack(VanityShirt, 1);
        Assert.Equal("copper_helmet", inv.ShownArmor(0)!.Id);
        Assert.Equal(VanityShirt, inv.ShownArmor(1));
        Assert.Null(inv.ShownArmor(2));

        inv.Accessories[0] = S("cobalt_shield");
        inv.Accessories[1] = S("obsidian_skull");
        inv.Accessories[5] = S("hermes_boots"); // locked in a Classic world
        inv.VanityAccessories[0] = new ItemStack(VanityCape, 1);
        inv.SetAccessoryHidden(1, true);
        // Functional (not hidden) first, vanity last so it wins where both draw in one place.
        Assert.Equal(new[] { "cobalt_shield", VanityCape.Id }, inv.ShownAccessories.Select(i => i.Id));
    }

    [Theory]
    [InlineData(Difficulty.Classic, false, 5)]
    [InlineData(Difficulty.Classic, true, 5)] // the heart's slot stays greyed out in a Classic world
    [InlineData(Difficulty.Journey, true, 5)]
    [InlineData(Difficulty.Expert, false, 5)]
    [InlineData(Difficulty.Expert, true, 6)]
    [InlineData(Difficulty.Master, false, 6)]
    [InlineData(Difficulty.Master, true, 7)]
    public void AccessorySlotsFollowDifficultyAndDemonHeart(Difficulty difficulty, bool heart, int slots)
    {
        Assert.Equal(slots, GameWorld.AccessorySlotsFor(difficulty, heart));
        var state = new PlayerState("tester") { ExtraAccessorySlot = heart };
        using var g = TestGame.Create(difficulty: difficulty);
        g.World.AddPlayer(state, g.Player.Position);
        var inv = state.Inventory;
        Assert.Equal(slots, inv.AccessorySlotsUnlocked);

        // Every unlocked slot takes an accessory and counts.
        var accessories = Accessories(slots);
        foreach (var a in accessories)
        {
            inv.Backpack[0] = new ItemStack(a, 1);
            Assert.True(inv.Equip(0));
        }
        Assert.Equal(accessories[slots - 1], inv.Accessories[slots - 1].Item);
        Assert.Equal(slots, inv.EquippedStacks.Count());
        Assert.True(inv.Backpack[0].IsEmpty);
    }

    [Fact]
    public void LockedAccessorySlotKeepsItsItemAndOnlyGivesItBack()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        var (shield, skull) = (S("cobalt_shield"), S("obsidian_skull"));
        inv.Accessories[5] = shield; // worn in a harder world, locked here
        Assert.True(inv.IsAccessorySlotLocked(5));
        Assert.DoesNotContain(shield, inv.EquippedStacks);

        // Nothing goes in (not even a swap), but the item comes out onto the cursor and is never lost.
        inv.Cursor = skull;
        Assert.Equal(ClickOutcome.None, g.World.ClickSlot(g.Player, new SlotRef(SlotArea.Accessories, 5), false, ClickMods.None, SlotContext.None));
        Assert.Equal(shield, inv.Accessories[5]);
        Assert.Equal(ClickOutcome.None, g.World.ClickSlot(g.Player, new SlotRef(SlotArea.Dyes, PlayerInventory.ArmorSlots + 5), false, ClickMods.None, SlotContext.None));
        inv.Backpack.Add(inv.Cursor);
        inv.Cursor = ItemStack.Empty;
        Assert.Equal(ClickOutcome.None, g.World.ClickSlot(g.Player, new SlotRef(SlotArea.Accessories, 5), true, ClickMods.None, SlotContext.None));
        Assert.Equal(ClickOutcome.Moved, g.World.ClickSlot(g.Player, new SlotRef(SlotArea.Accessories, 5), false, ClickMods.None, SlotContext.None));
        Assert.Equal(shield, inv.Cursor);
        Assert.True(inv.Accessories[5].IsEmpty);
    }

    [Fact]
    public void EquipFromRoutesVanityDyesAndHooks()
    {
        var p = new PlayerState("tester");
        var inv = p.Inventory;
        var uses = new ItemUseTable(TestPaths.Content);
        inv.Backpack[0] = new ItemStack(VanityShirt, 1);
        inv.Backpack[1] = new ItemStack(VanityCape, 1);
        inv.Backpack[2] = new ItemStack(RedDye, 5);
        inv.Backpack[3] = S("grappling_hook");
        inv.Backpack[4] = S("dual_hook");
        inv.Backpack[5] = S("wood");
        inv.Backpack[20] = S("diamond_hook");

        Assert.True(inv.Equip(0));
        Assert.Same(VanityShirt, inv.VanityArmor[1].Item);
        Assert.True(inv.Armor[1].IsEmpty);
        Assert.Equal(0, p.Defense); // vanity grants nothing
        Assert.True(inv.Equip(1));
        Assert.Same(VanityCape, inv.VanityAccessories[0].Item);
        Assert.True(inv.Equip(2));
        Assert.Equal(new ItemStack(RedDye, 5), inv.Dyes[0]);
        Assert.False(inv.Equip(5)); // wood is not worn

        // A worn hook is the one the grapple key throws, even with a longer-reaching one carried.
        Assert.NotNull(GrappleHook.Best(p, uses, out var carried));
        Assert.Same(Item("diamond_hook"), carried);
        Assert.True(inv.Equip(3));
        Assert.Same(Item("grappling_hook"), inv.EquippedHook);
        Assert.Equal(0, inv.CountAll(Item("grappling_hook")));
        Assert.Same(uses[Item("grappling_hook")].Grapple, GrappleHook.Best(p, uses, out var thrown));
        Assert.Same(Item("grappling_hook"), thrown);
        // Equipping another hook swaps the worn one back into that slot.
        Assert.True(inv.Equip(4));
        Assert.Same(Item("dual_hook"), inv.EquippedHook);
        Assert.Equal("grappling_hook", inv.Backpack[4].Item!.Id);

        // The slot filters agree with the routing.
        Assert.True(PlayerInventory.MiscFilter(MiscSlot.Hook)(Item("ivy_whip")));
        Assert.False(PlayerInventory.MiscFilter(MiscSlot.Mount)(Item("ivy_whip")));
        Assert.True(PlayerInventory.DyeFilter(RedDye));
        Assert.False(PlayerInventory.ArmorFilter(EquipSlot.Body)(VanityShirt));
        Assert.True(PlayerInventory.ArmorFilter(EquipSlot.Body, vanityRow: true)(VanityShirt));
        Assert.True(PlayerInventory.ArmorFilter(EquipSlot.Head, vanityRow: true)(Item("copper_helmet")));
        Assert.False(PlayerInventory.AccessoryFilter(inv.Accessories, 0)(VanityCape));
        Assert.Same(inv.Misc, inv.Resolve(SlotArea.Misc));
        Assert.Null(inv.Resolve(SlotArea.Container));
    }

    [Fact]
    public void FavoriteSurvivesTransferButNotChestAdd()
    {
        var inv = new PlayerInventory();
        var backpack = inv.Backpack;
        backpack[12] = S("torch", 20);
        backpack[13] = S("copper_pickaxe");
        backpack.SetFavorite(12, true);
        backpack.SetFavorite(14, true); // empty slots cannot be favourites
        Assert.True(backpack.IsFavorite(12));
        Assert.False(backpack.IsFavorite(14));

        // Moving and swapping carry the mark; the stack itself (equality, stacking) is untouched.
        Inventory.Transfer(backpack, 12, backpack, 20);
        Assert.True(backpack.IsFavorite(20));
        Assert.False(backpack.IsFavorite(12));
        Inventory.Transfer(backpack, 20, backpack, 13);
        Assert.True(backpack.IsFavorite(13));
        Assert.Equal("copper_pickaxe", backpack[20].Item!.Id);
        Assert.False(backpack.IsFavorite(20));
        Assert.Equal(S("torch", 20), backpack[13]);
        Assert.True(backpack[13].CanStackWith(S("torch", 5)));

        // Merging keeps the destination's mark; size changes keep it too.
        backpack[30] = S("torch", 5);
        Inventory.Transfer(backpack, 30, backpack, 13);
        Assert.Equal(25, backpack[13].Count);
        Assert.True(backpack.IsFavorite(13));
        Assert.False(backpack.IsFavorite(30));
        Inventory.Transfer(backpack, 13, inv.Ammo, 0);
        Assert.True(inv.Ammo.IsFavorite(0));
        Inventory.Transfer(inv.Ammo, 0, backpack, 13);

        // A chest never keeps the mark, and the emptied backpack slot forgets it.
        var chest = new Inventory(new ItemStack[10]);
        SlotActions.QuickMove(backpack, 13, chest);
        Assert.Equal(S("torch", 25), chest[0]);
        Assert.False(chest.IsFavorite(0));
        Assert.False(backpack.IsFavorite(13));
        backpack[13] = S("wood", 3);
        Assert.False(backpack.IsFavorite(13));

        // Another item replacing a favourite clears it.
        backpack.SetFavorite(13, true);
        backpack[13] = S("dirt", 3);
        Assert.False(backpack.IsFavorite(13));
    }

    [Fact]
    public void AccessoryEquipRotatesWhenFull()
    {
        var inv = new PlayerInventory();
        var accessories = Accessories(8);
        for (int i = 0; i < 5; i++)
        {
            inv.Backpack[0] = new ItemStack(accessories[i], 1);
            Assert.True(inv.Equip(0));
        }
        inv.Backpack[0] = new ItemStack(accessories[2], 1);
        Assert.False(inv.Equip(0)); // never two of the same accessory
        inv.Backpack[0] = new ItemStack(accessories[5], 1);
        Assert.True(inv.Equip(0));
        Assert.Equal(accessories[5], inv.Accessories[0].Item);
        Assert.Equal(accessories[0], inv.Backpack[0].Item); // swapped out
        inv.Backpack[1] = new ItemStack(accessories[6], 1);
        Assert.True(inv.Equip(1));
        Assert.Equal(accessories[6], inv.Accessories[1].Item);
        Assert.Equal(accessories[1], inv.Backpack[1].Item);
        Assert.True(inv.Accessories[5].IsEmpty); // locked slots are never filled

        // A copy left in a locked slot still blocks a second one, or both would count once the slot unlocks.
        inv.Accessories[6] = new ItemStack(accessories[7], 1);
        inv.Backpack[2] = new ItemStack(accessories[7], 1);
        Assert.False(inv.Equip(2));
        Assert.False(PlayerInventory.AccessoryFilter(inv.Accessories, 0)(accessories[7]));
    }

    private static ItemDef[] Accessories(int n) => TestPaths.Content.Items.All
        .Where(i => i.EquipSlot == EquipSlot.Accessory && i.Category != ItemCategory.Vanity)
        .OrderBy(i => i.Id, StringComparer.Ordinal).Take(n).ToArray();
}
