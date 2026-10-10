using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class TownServicesTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Summon(TestGame g, string id) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    private static long Purse(TestGame g) => Coins.Total(g.Player.State.Inventory);

    [Fact]
    public void PrefixesChangeAWeaponsStats()
    {
        using var g = Create();
        var sword = g.Content.Items.Get("gold_broadsword");
        var plain = new ItemStack(sword, 1);
        var ruthless = new ItemStack(sword, 1, g.Content.Prefixes.Get("ruthless"));
        var keen = new ItemStack(sword, 1, g.Content.Prefixes.Get("keen"));
        Assert.Equal((int)MathF.Round(sword.Damage * 1.18f), Prefixes.Damage(ruthless));
        Assert.Equal(sword.Damage, Prefixes.Damage(plain));
        Assert.Equal(sword.Crit + 3, Prefixes.Crit(keen));
        Assert.True(Prefixes.Value(ruthless) > Prefixes.Value(plain));
        Assert.False(Prefixes.CanHave(g.Content.Items.Get("wood")));
        Assert.Contains("accessory", Prefixes.Groups(g.Content.Items.Get("hermes_boots")));
    }

    [Fact]
    public void CraftedWeaponsUsuallyComeWithAPrefix()
    {
        using var g = Create();
        var book = new RecipeBook(g.Content);
        var inv = new Inventory(40);
        int prefixed = 0;
        var recipe = book.RecipesFor(g.Content.Items.Get("wooden_sword"))[0];
        for (int i = 0; i < 20; i++)
        {
            inv.Add(new ItemStack(g.Content.Items.Get("wood"), 7));
            Assert.True(book.Craft(recipe, inv, new HashSet<string> { "work_bench" }, new Conditions.ConditionContext()));
        }
        for (int i = 0; i < inv.Size; i++)
            if (inv[i].Item?.Id == "wooden_sword" && inv[i].Prefix != null) prefixed++;
        Assert.InRange(prefixed, 8, 20);
    }

    [Fact]
    public void AccessoryPrefixesApplyToTheWearer()
    {
        using var g = Create();
        var p = g.Player.State;
        p.Inventory.Accessories[0] = new ItemStack(g.Content.Items.Get("hermes_boots"), 1, g.Content.Prefixes.Get("guarding"));
        p.Recompute();
        Assert.Equal(g.Content.Items.Get("hermes_boots").Defense + 2, p.Defense);
    }

    [Fact]
    public void TheGoblinTinkererReforgesTheHeldItemForCoins()
    {
        using var g = Create();
        var goblin = Summon(g, "goblin_tinkerer");
        var inv = g.Player.State.Inventory;
        inv.Backpack[inv.SelectedHotbar] = new ItemStack(g.Content.Items.Get("gold_broadsword"), 1);
        Assert.Empty(Wallet.Give(inv, g.Content, 1000000));
        long cost = g.World.Services.ReforgeCost(goblin, g.Player);
        Assert.True(cost > 0);
        long before = Purse(g);
        Assert.Equal(TradeResult.Ok, g.World.Services.Reforge(goblin, g.Player, out var made));
        Assert.NotNull(made.Prefix);
        Assert.Equal(made, inv.Held);
        Assert.Equal(before - cost, Purse(g));
        // A second reforge never lands on the same prefix.
        var first = made.Prefix;
        Assert.Equal(TradeResult.Ok, g.World.Services.Reforge(goblin, g.Player, out made));
        Assert.NotEqual(first, made.Prefix);
        // Blocks cannot be reforged.
        inv.Backpack[inv.SelectedHotbar] = new ItemStack(g.Content.Items.Get("wood"), 5);
        Assert.Equal(0, g.World.Services.ReforgeCost(goblin, g.Player));
    }

    [Fact]
    public void ReforgeWorksOnTheServiceSlot()
    {
        using var g = Create();
        var goblin = Summon(g, "goblin_tinkerer");
        var inv = g.Player.State.Inventory;
        var held = inv.Held;
        var sword = new ItemStack(g.Content.Items.Get("gold_broadsword"), 1);
        inv.Service[0] = sword;
        Assert.Equal(TradeResult.TooPoor, g.World.Services.Reforge(goblin, g.Player, inv.Service, 0, out _));
        Assert.Equal(sword, inv.Service[0]);
        Assert.Empty(Wallet.Give(inv, g.Content, 1000000));
        long cost = g.World.Services.ReforgeCost(goblin, g.Player, inv.Service[0]);
        Assert.True(cost > 0);
        var reforged = new List<ItemStack>();
        g.World.Services.Reforged += (p, s) => reforged.Add(s);
        long before = Purse(g);
        Assert.Equal(TradeResult.Ok, g.World.Services.Reforge(goblin, g.Player, inv.Service, 0, out var made));
        Assert.NotNull(made.Prefix);
        Assert.Equal(made, inv.Service[0]);
        Assert.Equal(before - cost, Purse(g));
        Assert.Equal(new[] { made }, reforged);
        Assert.Equal(held, inv.Held); // the held item is not touched
        // The cost follows the new prefix's value; a second roll never lands on the same prefix.
        Assert.Equal(TradeResult.Ok, g.World.Services.Reforge(goblin, g.Player, inv.Service, 0, out var again));
        Assert.NotEqual(made.Prefix, again.Prefix);
        Assert.Equal(2, reforged.Count);
    }

    [Fact]
    public void CostIsZeroForUnprefixableItems()
    {
        using var g = Create();
        var goblin = Summon(g, "goblin_tinkerer");
        var merchant = Summon(g, "merchant");
        var inv = g.Player.State.Inventory;
        Assert.Empty(Wallet.Give(inv, g.Content, 1000000));
        var wood = new ItemStack(g.Content.Items.Get("wood"), 5);
        var sword = new ItemStack(g.Content.Items.Get("gold_broadsword"), 1);
        Assert.Equal(0, g.World.Services.ReforgeCost(goblin, g.Player, wood));
        Assert.Equal(0, g.World.Services.ReforgeCost(goblin, g.Player, ItemStack.Empty));
        Assert.Equal(0, g.World.Services.ReforgeCost(merchant, g.Player, sword)); // he does not reforge
        inv.Service[0] = wood;
        long before = Purse(g);
        Assert.Equal(TradeResult.NothingToDo, g.World.Services.Reforge(goblin, g.Player, inv.Service, 0, out _));
        Assert.Equal(wood, inv.Service[0]);
        Assert.Equal(before, Purse(g));
        // The reforge slot takes only what can carry a prefix: wood stays on the cursor, the sword goes in.
        inv.Service[0] = ItemStack.Empty;
        inv.Cursor = wood;
        var slot = new SlotRef(SlotArea.Service, 0);
        Assert.Equal(ClickOutcome.None, g.World.ClickSlot(g.Player, slot, false, ClickMods.None, default));
        Assert.True(inv.Service[0].IsEmpty);
        inv.Cursor = sword;
        Assert.Equal(ClickOutcome.Moved, g.World.ClickSlot(g.Player, slot, false, ClickMods.None, default));
        Assert.Equal(sword, inv.Service[0]);
        Assert.True(inv.Cursor.IsEmpty);
    }

    [Fact]
    public void ServiceSlotStowedOnClose()
    {
        using var g = Create();
        var inv = g.Player.State.Inventory;
        var sword = new ItemStack(g.Content.Items.Get("gold_broadsword"), 1, g.Content.Prefixes.Get("keen"));
        int carried = inv.CountAll(sword.Item!);
        inv.Service[0] = sword;
        Assert.True(inv.StowService().IsEmpty);
        Assert.True(inv.Service[0].IsEmpty);
        Assert.Equal(carried + 1, inv.CountAll(sword.Item!));
        Assert.True(inv.StowService().IsEmpty); // nothing to stow
        // A full backpack hands the stack back for dropping at the player's feet.
        var dirt = g.Content.Items.Get("dirt");
        for (int i = 0; i < inv.Backpack.Size; i++) inv.Backpack[i] = new ItemStack(dirt, dirt.MaxStack);
        inv.Service[0] = sword;
        Assert.Equal(sword, inv.StowService());
        Assert.True(inv.Service[0].IsEmpty);
    }

    [Fact]
    public void TheTaxCollectorGathersRentFromHousedResidents()
    {
        using var g = Create();
        var collector = Summon(g, "tax_collector");
        collector.Home = g.Floor + new Int3(3, 1, 3);
        Summon(g, "merchant").Home = g.Floor + new Int3(-3, 1, 3);
        Assert.Equal(0, g.World.Services.TaxesDue(collector)); // starts counting now
        g.World.Session.Clock.Advance(GameClock.TicksPerHour * 5);
        Assert.Equal(5 * 2 * 50, g.World.Services.TaxesDue(collector));
        long before = Purse(g);
        Assert.Equal(TradeResult.Ok, g.World.Services.CollectTaxes(collector, g.Player, out long amount));
        Assert.Equal(500, amount);
        Assert.Equal(before + 500, Purse(g));
        Assert.Equal(0, g.World.Services.TaxesDue(collector));
    }

    [Fact]
    public void TheAnglerWantsOneFishADay()
    {
        using var g = Create();
        var angler = Summon(g, "angler");
        var wanted = g.World.Services.AnglerWants(angler);
        Assert.NotNull(wanted);
        Assert.Equal(TradeResult.Unavailable, g.World.Services.TurnInFish(angler, g.Player, out _));
        g.Player.State.Inventory.Backpack.Add(new ItemStack(wanted!, 1));
        long before = Purse(g);
        Assert.Equal(TradeResult.Ok, g.World.Services.TurnInFish(angler, g.Player, out _));
        Assert.True(Purse(g) > before);
        Assert.Null(g.World.Services.AnglerWants(angler)); // done for today
        g.World.Session.Clock.Advance(GameClock.TicksPerDay);
        Assert.NotNull(g.World.Services.AnglerWants(angler));
    }

    [Fact]
    public void ResidentsArriveAsTheWorldProgresses()
    {
        using var g = Create();
        var ctx = g.World.Town.TownContext(g.Player);
        bool Arrives(string id) => Conditions.ConditionCompiler.CompileAll(g.Content.TownNpcs.Get(id).Arrival).Evaluate(g.World.Town.TownContext(g.Player));
        Assert.False(Arrives("goblin_tinkerer"));
        g.World.Progression.Set("defeated_goblin_army");
        Assert.False(Arrives("goblin_tinkerer")); // he must be found and freed first
        g.World.Progression.Set(TownRescue.FlagFor("goblin_tinkerer"));
        Assert.True(Arrives("goblin_tinkerer"));
        Assert.False(Arrives("arms_dealer"));
        g.Player.State.Inventory.Backpack.Add(new ItemStack(g.Content.Items.Get("musket_ball"), 10));
        Assert.True(Arrives("arms_dealer"));
        Assert.False(Arrives("cyborg"));
        g.World.Progression.Set("defeated_plantera");
        Assert.True(Arrives("cyborg"));
    }
}
