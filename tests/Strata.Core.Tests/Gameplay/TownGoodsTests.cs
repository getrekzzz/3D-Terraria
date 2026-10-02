using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-15..P5-18: what the residents' new goods do (grass seeds, auras, food, ammo).</summary>
public class TownGoodsTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        return g;
    }

    internal static TileObjectInstance Place(TestGame g, string id, int dx, int dz)
    {
        var obj = new TileObjectInstance(g.Content.TileObjects.Get(id), g.Floor + new Int3(dx, 1, dz), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(obj));
        return obj;
    }

    [Fact]
    public void GrassSeedsTurnDirtToGrass()
    {
        using var g = Arena();
        var cell = g.Floor + new Int3(0, 0, 3);
        var blocks = g.Content.Blocks;
        g.World.Editor.SetBlock(cell, (ushort)blocks.Get("dirt").RuntimeId);
        g.Hold("grass_seeds", 5);
        g.AimAt(TestGame.CellCenter(cell));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal("grass", blocks[g.World.Session.World.GetBlock(cell)].Id);
        Assert.Equal(4, g.Player.State.Inventory.Backpack[0].Count);

        // Seeds only take on their soil: the grass is not dirt any more, and stone never was.
        g.Player.UseCooldown = 0;
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal(4, g.Player.State.Inventory.Backpack[0].Count);
    }

    [Fact]
    public void TheSunflowerMakesNearbyPlayersHappy()
    {
        using var g = Arena();
        Place(g, "sunflower", 3, 3);
        g.Ticks(61);
        Assert.Contains(g.Player.State.Buffs.Active, b => b.Def.Id == "happy");
    }

    [Fact]
    public void TheBewitchingTableGivesAMinionSlot()
    {
        using var g = Arena();
        Place(g, "bewitching_table", 2, 2);
        g.Ticks(61);
        Assert.Contains(g.Player.State.Buffs.Active, b => b.Def.Id == "bewitched");
        g.Player.State.Recompute();
        Assert.Equal(1f, g.Player.State.Effects.Stats.Bonus(Strata.Core.Gameplay.Stats.StatIds.MaxMinions));
    }

    [Fact]
    public void AHerbBagOpensIntoHerbs()
    {
        using var g = Arena();
        g.Hold("herb_bag", 1);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.True(g.Player.State.Inventory.Backpack[0].IsEmpty);
    }

    [Fact]
    public void AGlowstickIsThrownAndUsedUp()
    {
        using var g = Arena();
        g.Hold("glowstick", 3);
        g.AimAt(g.Player.Eye + new System.Numerics.Vector3(0, 0.3f, 1));
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, g.Target(), freshPress: true));
        Assert.Equal(2, g.Player.State.Inventory.Backpack[0].Count);
    }

    [Fact]
    public void AleMakesYouTipsy()
    {
        using var g = Arena();
        g.Hold("ale", 2);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.Contains(g.Player.State.Buffs.Active, b => b.Def.Id == "tipsy");
    }
}
