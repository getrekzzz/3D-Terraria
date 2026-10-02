using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-21: the Stylist's haircut and the hair dyes.</summary>
public class StylistTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Stylist(TestGame g) =>
        g.World.Town.Summon(g.Content.TownNpcs.Get("stylist"), g.World.Navigator.NearestStandable(g.Floor + new Int3(3, 1, 3), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    [Fact]
    public void HaircutChangesLookAndCharges()
    {
        using var g = Create();
        var npc = Stylist(g);
        var inv = g.Player.State.Inventory;
        Wallet.Give(inv, g.Content, 100000);
        int changed = 0;
        g.Player.State.LookChanged += () => changed++;
        long cost = g.World.Services.HaircutCost(npc, g.Player, "mohawk", "#3a6ab0");
        Assert.True(cost > 0);
        Assert.Equal(TradeResult.Ok, g.World.Services.Haircut(npc, g.Player, "mohawk", "#3A6AB0"));
        Assert.Equal("mohawk", g.Player.State.Look.HairStyle);
        Assert.Equal("#3a6ab0", g.Player.State.Look.HairColor);
        Assert.Equal(100000 - cost, Coins.Total(inv));
        Assert.Equal(1, changed);
        // The same look again costs nothing and does nothing.
        Assert.Equal(TradeResult.NothingToDo, g.World.Services.Haircut(npc, g.Player, "mohawk", "#3a6ab0"));
    }

    [Fact]
    public void AnInvalidStyleIsRefusedAndNotCharged()
    {
        using var g = Create();
        var npc = Stylist(g);
        var inv = g.Player.State.Inventory;
        Wallet.Give(inv, g.Content, 100000);
        Assert.Equal(TradeResult.Refused, g.World.Services.Haircut(npc, g.Player, "no_such_style", "#3a6ab0"));
        Assert.Equal(TradeResult.Refused, g.World.Services.Haircut(npc, g.Player, "mohawk", "blue"));
        Assert.Equal(100000, Coins.Total(inv));
        Assert.Equal("short", g.Player.State.Look.HairStyle);
    }

    [Fact]
    public void APennilessPlayerIsTooPoor()
    {
        using var g = Create();
        var npc = Stylist(g);
        Assert.Equal(TradeResult.TooPoor, g.World.Services.Haircut(npc, g.Player, "mohawk", "#3a6ab0"));
        Assert.Equal("short", g.Player.State.Look.HairStyle);
    }

    [Fact]
    public void LifeDyeAtFullAndLowLifeGivesDifferentColours()
    {
        var dye = new HairDyeParams { Mode = HairDyeMode.Life };
        var natural = HairRgb.Bytes(90, 60, 30);
        var full = HairDyes.Color(dye, natural, new HairDyeInputs(1f, 1f, WorldLayer.Surface, 12f, 0, 0, 0));
        var low = HairDyes.Color(dye, natural, new HairDyeInputs(0.1f, 1f, WorldLayer.Surface, 12f, 0, 0, 0));
        Assert.NotEqual(full, low);
        Assert.True(full.R > low.R);
    }

    [Fact]
    public void DepthDyeChangesWithDepth()
    {
        var dye = new HairDyeParams { Mode = HairDyeMode.Depth };
        var natural = HairRgb.Bytes(90, 60, 30);
        var surface = HairDyes.Color(dye, natural, new HairDyeInputs(1f, 1f, WorldLayer.Surface, 12f, 0, 0, 0));
        var cavern = HairDyes.Color(dye, natural, new HairDyeInputs(1f, 1f, WorldLayer.Cavern, 12f, 0, 0, 0));
        var hell = HairDyes.Color(dye, natural, new HairDyeInputs(1f, 1f, WorldLayer.Underworld, 12f, 0, 0, 0));
        Assert.NotEqual(surface, cavern);
        Assert.NotEqual(cavern, hell);
    }

    [Fact]
    public void UsingAHairDyeDyesTheHairAndTheRemoverWashesItOut()
    {
        using var g = Create();
        var inv = g.Player.State.Inventory;
        inv.Select(9);
        inv.Backpack[9] = new ItemStack(g.Content.Items.Get("life_hair_dye"), 1);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("life_hair_dye", g.Player.State.Look.HairDye);
        Assert.True(inv.Backpack[9].IsEmpty);
        Assert.NotEqual(HairDyes.TryParseHex(g.Player.State.Look.HairColor, out var n) ? n : default, HairDyes.ColorFor(g.Player, g.World));
        g.Ticks(120); // the use cooldown
        inv.Backpack[9] = new ItemStack(g.Content.Items.Get("hair_dye_remover"), 1);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Null(g.Player.State.Look.HairDye);
    }

    [Fact]
    public void HairDyeSurvivesASave()
    {
        var state = PlayerState.CreateNew("Ada", TestPaths.Content);
        state.Look.HairDye = "money_hair_dye";
        var dir = TestWorlds.TempDir();
        GameWorld.WritePlayer(dir, state);
        Assert.Equal("money_hair_dye", GameWorld.LoadOrCreatePlayer(dir, "Ada", TestPaths.Content).Look.HairDye);
    }

    [Fact]
    public void TheStylistSellsHairDyes()
    {
        using var g = Create();
        var npc = Stylist(g);
        Assert.NotNull(npc.Def.FindService(TownServiceSpecs.Haircut));
        Assert.Contains(npc.Def.Shop, e => e.Item == "life_hair_dye");
    }
}
