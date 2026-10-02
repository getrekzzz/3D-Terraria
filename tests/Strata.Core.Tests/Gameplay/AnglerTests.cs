using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Persistence;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class AnglerTests
{
    private static (TestGame G, TownNpc Angler) Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var at = g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        return (g, g.World.Town.Summon(g.Content.TownNpcs.Get("angler"), at));
    }

    private static TradeResult Quest(TestGame g, TownNpc angler)
    {
        var wanted = g.World.Services.AnglerWants(angler)!;
        g.Player.State.Inventory.Backpack.Add(new ItemStack(wanted, 1));
        var result = g.World.Services.TurnInFish(angler, g.Player, out _);
        g.World.Session.Clock.Advance(GameClock.TicksPerDay);
        return result;
    }

    [Fact]
    public void CounterIncrementsAndSaves()
    {
        var (g, angler) = Create();
        using var _ = g;
        int raised = 0;
        g.World.Services.AnglerQuestDone += (_, n) => raised = n;
        for (int i = 0; i < 60; i++) g.World.Tick(); // the achievement tracker attaches on its first sweep
        Assert.Equal(TradeResult.Ok, Quest(g, angler));
        Assert.Equal(TradeResult.Ok, Quest(g, angler));
        Assert.Equal(2, g.Player.State.AnglerQuests);
        Assert.Equal(2, raised);
        Assert.Equal(2, g.Player.State.Journal.Counter("angler_quests"));
        var loaded = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content);
        Assert.Equal(2, loaded.AnglerQuests);
        Assert.True(g.Player.State.Journal.Achieved("servant_in_training"));
    }

    [Fact]
    public void MilestoneRewardAtFive()
    {
        var (g, angler) = Create();
        using var _ = g;
        var potion = g.Content.Items.Get("crate_potion");
        for (int i = 0; i < 4; i++) Quest(g, angler);
        int before = g.Player.State.Inventory.CountAll(potion);
        Quest(g, angler);
        Assert.True(g.Player.State.Inventory.CountAll(potion) >= before + 2);
    }

    [Fact]
    public void ShopUnlocksByCount()
    {
        var (g, angler) = Create();
        using var _ = g;
        int Stock() => g.World.Commerce.Stock(angler, g.Player).Count;
        Assert.Equal(0, Stock());
        g.Player.State.AnglerQuests = 1;
        int one = Stock();
        Assert.True(one >= 2);
        g.Player.State.AnglerQuests = 20;
        Assert.True(Stock() > one);
        Assert.Contains(g.World.Commerce.Stock(angler, g.Player), o => o.Item.Id == "tuna_plaque");
    }
}
