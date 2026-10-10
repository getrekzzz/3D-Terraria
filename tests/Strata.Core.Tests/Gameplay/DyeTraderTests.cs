using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-22: the Dye Trader swaps a Strange Plant for a rare dye.</summary>
public class DyeTraderTests
{
    private static (TestGame, TownNpc) Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var npc = g.World.Town.Summon(g.Content.TownNpcs.Get("dye_trader"), g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);
        return (g, npc);
    }

    [Fact]
    public void TradeConsumesPlantGivesDye()
    {
        var (g, npc) = Create();
        using var game = g;
        var inv = g.Player.State.Inventory;
        var plant = g.Content.Items.Get("strange_plant_3");
        inv.Backpack.Add(new ItemStack(plant, 2));
        Assert.True(g.World.Services.CanTradeStrangePlant(npc, g.Player));
        Assert.Equal(TradeResult.Ok, g.World.Services.TradeStrangePlant(npc, g.Player, out var dye));
        Assert.True(dye.Item!.HasTag("rare_dye"));
        Assert.Equal(1, inv.Backpack.Count(plant));
        Assert.Equal(1, inv.CountAll(dye.Item!));
    }

    [Fact]
    public void WithoutAPlantThereIsNothingToDo()
    {
        var (g, npc) = Create();
        using var game = g;
        Assert.False(g.World.Services.CanTradeStrangePlant(npc, g.Player));
        Assert.Equal(TradeResult.NothingToDo, g.World.Services.TradeStrangePlant(npc, g.Player, out _));
    }
}
