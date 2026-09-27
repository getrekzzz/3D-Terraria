using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class GearTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Liquids.Enabled = false;
        return g;
    }

    [Fact]
    public void ABottomlessBucketNeverRunsDry()
    {
        using var g = Arena();
        g.Hold("bottomless_water_bucket");
        for (int i = 0; i < 3; i++)
        {
            var target = g.Floor + new Int3(-4, 1, i * 2);
            g.Player.UseCooldown = 0;
            g.AimAt(TestGame.CellCenter(target) - new Vector3(0, 0.2f, 0));
            Assert.Equal(UseOutcome.PouredLiquid, g.World.ItemUse.Use(g.Player, null, freshPress: true));
            Assert.Equal("bottomless_water_bucket", g.Player.State.Inventory.Held.Item!.Id);
        }
    }

    [Fact]
    public void TheSpongeSoaksUpWaterWithoutFilling()
    {
        using var g = Arena();
        var cell = g.Floor + new Int3(4, 1, 0);
        g.World.Editor.FillLiquid(cell, cell, g.Content.Liquids.Get("water"), LiquidCode.MaxLevel);
        g.Hold("super_absorbant_sponge");
        g.AimAt(TestGame.CellCenter(cell) - new Vector3(0, 0.2f, 0));
        Assert.Equal(UseOutcome.CollectedLiquid, g.World.ItemUse.Use(g.Player, null, freshPress: true));
        Assert.Equal(0, g.World.Session.World.GetLiquid(cell.X, cell.Y, cell.Z));
        Assert.Equal("super_absorbant_sponge", g.Player.State.Inventory.Held.Item!.Id);
    }

    [Fact]
    public void AMotherSlimeBurstsIntoBabies()
    {
        using var g = Arena();
        var mother = g.World.Npcs.Spawn(g.Content.Enemies.Get("mother_slime"), g.Player.Position + new Vector3(0, 0, 6))!;
        g.World.Npcs.Kill(mother);
        Assert.Equal(3, g.World.Npcs.All.Count(n => n.Def.Id == "baby_slime"));
    }

    [Fact]
    public void LifeCrystalsBreakIntoTheItem()
    {
        using var g = Arena();
        var rock = new TileObjectInstance(g.Content.TileObjects.Get("life_crystal_rock"), g.Floor + new Int3(3, 1, 3), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(rock));
        var pick = g.Content.Items.Get("pickaxe_axe");
        var result = ObjectHitResult.Damaged;
        for (int i = 0; i < 10 && result != ObjectHitResult.Broken; i++) result = g.World.Editor.HitObject(rock, pick);
        Assert.Equal(ObjectHitResult.Broken, result);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item?.Id == "life_crystal");
    }
}
