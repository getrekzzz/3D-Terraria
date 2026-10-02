using Strata.Core.Commands;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class PurseAndAmmoTests
{
    private static ItemStack S(string id, int count = 1) => new(TestPaths.Content.Items.Get(id), count);

    private static PlayerState NewOwner() => PlayerState.CreateNew("purse", TestPaths.Content);

    private static PlayerInventory NewInventory() => NewOwner().Inventory;

    private static int CountIn(Inventory inv, string id) => inv.Count(TestPaths.Content.Items.Get(id));

    [Fact]
    public void CoinsGoToCoinSlotsAndAutoConvert()
    {
        var inv = NewInventory();
        int backpackStacks = inv.Backpack.Stacks.Count();
        Assert.True(inv.Pickup(S("copper_coin", 250)).IsEmpty);
        Assert.Equal(2, CountIn(inv.Coins, "silver_coin"));
        Assert.Equal(50, CountIn(inv.Coins, "copper_coin"));
        Assert.Equal(2, inv.Coins.Stacks.Count());
        Assert.Equal(backpackStacks, inv.Backpack.Stacks.Count()); // nothing reached the backpack
        Assert.Equal(250, Coins.Total(inv));

        // 50 more copper make a third silver; the copper slot empties and its silver neighbour keeps its place.
        int silverSlot = Slot(inv.Coins, "silver_coin");
        inv.Coins.SetFavorite(silverSlot, true);
        Assert.True(inv.Pickup(S("copper_coin", 50)).IsEmpty);
        Assert.Equal(S("silver_coin", 3), inv.Coins[silverSlot]);
        Assert.True(inv.Coins.IsFavorite(silverSlot));
        Assert.Equal(0, CountIn(inv.Coins, "copper_coin"));

        // A full house of denominations still takes a stack that needs converting midway.
        inv.Coins[0] = S("platinum_coin", 1);
        inv.Coins[1] = S("gold_coin", 99);
        inv.Coins[2] = S("silver_coin", 99);
        inv.Coins[3] = S("copper_coin", 99);
        Assert.True(inv.Pickup(S("copper_coin", 100)).IsEmpty);
        Assert.Equal(2_000_099, Coins.Total(inv.Coins));
        Assert.Equal(S("platinum_coin", 2), inv.Coins[0]);
        Assert.Equal(99, CountIn(inv.Coins, "copper_coin"));
        Assert.Equal(2, inv.Coins.Stacks.Count());
    }

    [Fact]
    public void ConversionOverflowsIntoBackpack()
    {
        var inv = NewInventory();
        // A full platinum stack, 101 gold (one more platinum and 1 gold), silver and copper: five stacks for four slots.
        int max = S("platinum_coin").Item!.MaxStack;
        inv.Coins[0] = S("platinum_coin", max);
        inv.Coins[1] = S("gold_coin", 101);
        inv.Coins[2] = S("silver_coin", 5);
        inv.Coins[3] = S("copper_coin", 7);
        long before = Coins.Total(inv);
        Assert.True(Coins.Normalize(inv, TestPaths.Content));
        Assert.Equal(before, Coins.Total(inv));
        Assert.Equal(new[] { S("platinum_coin", max), S("gold_coin", 1), S("silver_coin", 5), S("copper_coin", 7) }, inv.Coins.ToArray());
        // The second platinum stack no longer fits the coin slots and goes to the backpack.
        Assert.Equal(1, CountIn(inv.Backpack, "platinum_coin"));

        // With the backpack full too, nothing changes.
        var full = NewInventory();
        for (int i = 0; i < full.Backpack.Size; i++) full.Backpack[i] = S("dirt", 9999);
        full.Coins[0] = S("platinum_coin", max);
        full.Coins[1] = S("gold_coin", 101);
        full.Coins[2] = S("silver_coin", 5);
        full.Coins[3] = S("copper_coin", 7);
        var snapshot = full.Coins.ToArray();
        Assert.False(Coins.Normalize(full, TestPaths.Content));
        Assert.Equal(snapshot, full.Coins.ToArray());
    }

    [Fact]
    public void WalletSpendsAcrossCoinSlotsAndBackpack()
    {
        var inv = NewInventory();
        inv.Coins[0] = S("silver_coin", 3);
        inv.Backpack[30] = S("gold_coin", 1);
        Assert.Equal(10_300, Coins.Total(inv));
        Assert.False(Wallet.TrySpend(inv, TestPaths.Content, 10_301, out _));
        Assert.Equal(10_300, Coins.Total(inv));

        // The coin slots pay what they can: the backpack's gold stays untouched.
        Assert.True(Wallet.TrySpend(inv, TestPaths.Content, 250, out var change));
        Assert.Empty(change);
        Assert.Equal(S("gold_coin", 1), inv.Backpack[30]);
        Assert.Equal(50, Coins.Total(inv.Coins));

        // More than the coin slots hold: the backpack's gold is broken too.
        Assert.True(Wallet.TrySpend(inv, TestPaths.Content, 1_000, out change));
        Assert.Empty(change);
        Assert.Equal(9_050, Coins.Total(inv));
        Assert.Equal(0, Coins.Total(inv.Backpack));
    }

    [Fact]
    public void ChangeReturnsToCoinSlots()
    {
        var inv = NewInventory();
        inv.Backpack[30] = S("platinum_coin", 1);
        Assert.True(Wallet.TrySpend(inv, TestPaths.Content, 1, out var change));
        Assert.Empty(change);
        Assert.Equal(0, Coins.Total(inv.Backpack));
        Assert.Equal(99, CountIn(inv.Coins, "gold_coin"));
        Assert.Equal(99, CountIn(inv.Coins, "silver_coin"));
        Assert.Equal(99, CountIn(inv.Coins, "copper_coin"));

        // Earnings join the coin slots and convert as they fill.
        Assert.Empty(Wallet.Give(inv, TestPaths.Content, 1));
        Assert.Equal(S("platinum_coin", 1), inv.Coins.Stacks.Single());
    }

    [Fact]
    public void ArrowsGoToAmmoSlots()
    {
        var inv = NewInventory();
        Assert.True(inv.Pickup(S("wooden_arrow", 50)).IsEmpty);
        Assert.Equal(S("wooden_arrow", 50), inv.Ammo[0]);
        Assert.Equal(0, CountIn(inv.Backpack, "wooden_arrow"));
        Assert.True(inv.Pickup(S("musket_ball", 20)).IsEmpty);
        Assert.Equal(S("musket_ball", 20), inv.Ammo[1]);

        // Full ammo slots send the rest to the backpack; Fallen Stars (a material) never fill them.
        inv.Ammo[2] = S("flaming_arrow", 10);
        inv.Ammo[3] = S("unholy_arrow", 10);
        Assert.True(inv.Pickup(S("jesters_arrow", 5)).IsEmpty);
        Assert.Equal(5, CountIn(inv.Backpack, "jesters_arrow"));
        Assert.True(inv.Pickup(S("wooden_arrow", 5)).IsEmpty);
        Assert.Equal(S("wooden_arrow", 55), inv.Ammo[0]);
        var star = new PlayerInventory();
        Assert.True(star.Pickup(S("fallen_star", 3)).IsEmpty);
        Assert.Equal(3, CountIn(star.Backpack, "fallen_star"));
        Assert.Empty(star.Ammo.Stacks);

        // A full backpack still takes arrows into the ammo slots, but nothing else.
        for (int i = 0; i < inv.Backpack.Size; i++) inv.Backpack[i] = S("dirt", 9999);
        Assert.True(inv.CanTake(S("wooden_arrow", 100)));
        Assert.False(inv.CanTake(S("torch", 1)));
        Assert.True(inv.CanTake(S("copper_coin", 10)));
    }

    [Fact]
    public void FindAmmoPrefersAmmoSlotsTopToBottom()
    {
        var inv = NewInventory();
        Assert.Null(inv.FindAmmo("arrow"));
        inv.Backpack[5] = S("wooden_arrow", 10);
        Assert.Equal((inv.Backpack, 5), inv.FindAmmo("arrow"));
        inv.Ammo[3] = S("flaming_arrow", 10);
        inv.Ammo[1] = S("unholy_arrow", 10);
        inv.Ammo[0] = S("musket_ball", 10);
        Assert.Equal((inv.Ammo, 1), inv.FindAmmo("arrow"));
        Assert.Equal((inv.Ammo, 0), inv.FindAmmo("bullet"));
        Assert.Null(inv.FindAmmo("rocket"));
    }

    [Fact]
    public void CountAmmoSumsEverySlot()
    {
        var inv = NewInventory();
        inv.Ammo[0] = S("wooden_arrow", 30);
        inv.Ammo[2] = S("flaming_arrow", 7);
        inv.Backpack[12] = S("wooden_arrow", 5);
        inv.Backpack[13] = S("musket_ball", 9);
        Assert.Equal(42, inv.CountAmmo("arrow"));
        Assert.Equal(9, inv.CountAmmo("bullet"));
        Assert.Equal(0, inv.CountAmmo("rocket"));
    }

    [Fact]
    public void BowsShootFromTheAmmoSlotsFirst()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        g.Hold("wooden_bow");
        inv.Backpack[20] = S("wooden_arrow", 3);
        inv.Ammo[2] = S("flaming_arrow", 2);
        var shots = new List<ShotRequest>();
        g.World.ItemUse.Shot += shots.Add;
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("flaming_arrow", shots.Single().Ammo!.Id);
        Assert.Equal(1, inv.Ammo[2].Count);
        Assert.Equal(3, inv.Backpack[20].Count);
    }

    [Fact]
    public void ProbesAndClearSeeEverySlot()
    {
        using var g = TestGame.Create();
        var registry = BuiltinCommands.CreateRegistry();
        var ctx = new CommandContext(g.World, new HeadlessCommandHost()) { Registry = registry };
        var inv = g.Player.State.Inventory;
        Assert.True(inv.Pickup(S("copper_coin", 250)).IsEmpty);
        Assert.True(inv.Pickup(S("wooden_arrow", 50)).IsEmpty);
        inv.Trash[0] = S("wooden_arrow", 4);
        inv.Backpack[30] = S("silver_coin", 1);
        Assert.Equal(350, ctx.ReadProbe("inv.coins"));
        Assert.Equal(3, ctx.ReadProbe("inv.silver_coin"));
        Assert.Equal(54, ctx.ReadProbe("inv.wooden_arrow"));
        Assert.True(registry.Execute(ctx, "clear").Ok);
        Assert.Equal(0, ctx.ReadProbe("inv.coins"));
        Assert.Equal(0, ctx.ReadProbe("inv.wooden_arrow"));
    }

    [Fact]
    public void CraftingUsesArrowsInTheAmmoSlots()
    {
        var inv = NewInventory();
        var arrow = TestPaths.Content.Items.Get("wooden_arrow");
        Assert.True(inv.Pickup(S("wooden_arrow", 12)).IsEmpty);
        Assert.Equal(0, inv.Backpack.Count(arrow)); // all in the ammo slots; the starting torches are in the backpack
        var book = new Strata.Core.Gameplay.Crafting.RecipeBook(TestPaths.Content);
        var recipe = book.RecipesFor(TestPaths.Content.Items.Get("flaming_arrow")).First(r => r.Inputs.Any(i => i.Accepts.Contains(arrow)));
        var stations = new HashSet<string>(recipe.Def.Stations);
        var ctx = new Strata.Core.Conditions.ConditionContext();
        Assert.False(book.CanCraft(recipe, inv.Backpack, stations, ctx));
        Assert.True(book.CanCraft(recipe, inv, stations, ctx));
        Assert.True(book.Craft(recipe, inv, stations, ctx));
        Assert.Equal(2, inv.CountAll(arrow));
        Assert.Equal(recipe.Def.Count, CountIn(inv.Ammo, "flaming_arrow")); // the output is picked up into the ammo slots
    }

    [Fact]
    public void LoadedCharacterConvertsCoins()
    {
        var state = NewOwner();
        state.Inventory.Backpack[25] = S("gold_coin", 2);
        var loaded = PlayerSave.Deserialize(PlayerSave.From(state).Serialize()).ToState(TestPaths.Content);
        Assert.Same(TestPaths.Content, loaded.Inventory.Content);
        Assert.Equal(20_000, Coins.Total(loaded.Inventory)); // old saves keep coins in the backpack; they still count
        Assert.True(loaded.Inventory.Pickup(S("silver_coin", 100)).IsEmpty);
        Assert.Equal(S("gold_coin", 1), loaded.Inventory.Coins[0]);
    }

    private static int Slot(Inventory inv, string id)
    {
        for (int i = 0; i < inv.Size; i++)
            if (inv[i].Item?.Id == id) return i;
        return -1;
    }
}
