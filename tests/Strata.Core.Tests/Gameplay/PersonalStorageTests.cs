using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;
using Xunit;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P3-35: the Piggy Bank and Safe open the character's own storage, wherever they stand.</summary>
public class PersonalStorageTests
{
    private static TileObjectInstance Place(TestGame g, string id, Int3 offset)
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), g.Floor + offset, 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    private static Inventory Open(TestGame g, TileObjectInstance obj)
    {
        Inventory? shown = null;
        void OnOpen(PlayerEntity p, TileObjectInstance o, Inventory inv) => shown = inv;
        g.World.Interaction.ContainerOpened += OnOpen;
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, obj));
        g.World.Interaction.ContainerOpened -= OnOpen;
        return shown!;
    }

    [Fact]
    public void EveryPiggyBankOpensTheSameSlotsAndBreakingSpillsNothing()
    {
        using var g = TestGame.Create();
        var one = Place(g, "piggy_bank", new Int3(3, 1, 0));
        var two = Place(g, "piggy_bank", new Int3(-4, 1, 2));
        Assert.Null(one.Container);
        var dirt = g.Content.Items.Get("dirt");
        g.Player.State.Inventory.Backpack[20] = new ItemStack(dirt, 10);
        Assert.True(g.World.ChestAction(g.Player, one, ChestOp.DepositAll) >= 10);
        var bank = Open(g, two);
        Assert.Equal(PlayerStateBankSize, bank.Size);
        Assert.Equal(10, bank.Count(dirt));
        Assert.Same(g.Player.State.Bank("piggy_bank"), bank);
        // A Safe is a separate storage.
        Assert.Equal(0, Open(g, Place(g, "safe", new Int3(0, 1, 4))).Count(dirt));

        // The contents live on the character: a save round trip keeps them.
        var loaded = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content);
        Assert.Equal(10, loaded.Bank("piggy_bank").Count(dirt));
        // An older save without banks loads with an empty one.
        Assert.Empty(PlayerSave.Deserialize("{\"format\":\"strata-player\",\"version\":1,\"name\":\"old\"}").ToState(g.Content).Banks);

        // Breaking a bank drops only the bank item; the dirt stays in the storage.
        g.Hold("copper_pickaxe");
        ObjectHitResult hit;
        int swings = 0;
        do hit = g.World.Editor.HitObject(one, g.Player.State.Inventory.Held.Item);
        while (hit != ObjectHitResult.Broken && ++swings < 10);
        Assert.Equal(ObjectHitResult.Broken, hit);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "piggy_bank");
        Assert.DoesNotContain(g.World.Drops.Items, d => d.Stack.Item == dirt);
        Assert.Equal(10, g.Player.State.Bank("piggy_bank").Count(dirt));
    }

    private const int PlayerStateBankSize = Strata.Core.Gameplay.Player.PlayerState.BankSize;

    [Fact]
    public void ShopsTakeCoinsKeptInThePiggyBank()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var merchant = g.World.Town.Summon(g.Content.TownNpcs.Get("merchant"),
            g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
        var inv = g.Player.State.Inventory;
        inv.Backpack.Remove(d => d.HasTag("coin"), int.MaxValue);
        inv.Coins.Remove(d => d.HasTag("coin"), int.MaxValue);
        var piggy = g.Content.Items.Get("piggy_bank");
        Assert.Contains(g.World.Commerce.Stock(merchant, g.Player), o => o.Item == piggy);
        Assert.Equal(TradeResult.TooPoor, g.World.Commerce.Buy(merchant, g.Player, piggy));

        // 30 silver in the purse, 2 gold in the bank: the purse goes first, the bank covers the rest, change to the purse.
        Assert.Empty(Wallet.Give(inv, g.Content, 3000));
        Assert.Empty(Wallet.Give(g.Player.State.Bank("piggy_bank"), g.Content, 20000));
        long price = g.World.Commerce.Stock(merchant, g.Player).First(o => o.Item == piggy).Price;
        Assert.Equal(TradeResult.Ok, g.World.Commerce.Buy(merchant, g.Player, piggy));
        Assert.Equal(1, inv.Backpack.Count(piggy));
        Assert.Equal(23000 - price, Coins.Total(inv) + Coins.Total(g.Player.State.Bank("piggy_bank")));
        Assert.Equal(0, Coins.Total(g.Player.State.Bank("piggy_bank")));
        Assert.Equal(23000 - price, Wallet.Funds(inv, g.Player.State.BanksForPaying()));
    }
}
