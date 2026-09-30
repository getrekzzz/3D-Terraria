using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Physics;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

public class CreatureTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static Strata.Core.Gameplay.Entities.Npc Spawn(TestGame g, string id, Vector3 feet) =>
        g.World.Npcs.Spawn(g.Content.Enemies.Get(id), feet) ?? throw new InvalidOperationException("spawn failed");

    [Fact]
    public void RangedCreaturesFireHostileProjectilesThatHurtPlayers()
    {
        using var g = Create();
        var hornet = Spawn(g, "hornet", g.Player.Position + new Vector3(4f, 2f, 0));
        int life = g.Player.State.Vitals.Life;
        bool fired = false;
        for (int i = 0; i < 600 && g.Player.State.Vitals.Life == life; i++)
        {
            g.World.Tick();
            fired |= g.World.Projectiles.All.Any(p => !p.Def.Friendly);
        }
        Assert.True(fired);
        Assert.True(g.Player.State.Vitals.Life < life);
        Assert.True(hornet.Alive); // its own shots never hit creatures
    }

    [Fact]
    public void PausedCreaturesHoldStill()
    {
        using var g = Create();
        var hornet = Spawn(g, "hornet", g.Player.Position + new Vector3(4f, 2f, 0));
        g.World.Npcs.Paused = true;
        var at = hornet.Position;
        int age = hornet.Age;
        for (int i = 0; i < 60; i++) g.World.Tick();
        Assert.Equal(at, hornet.Position);
        Assert.Equal(age, hornet.Age);
        g.World.Npcs.Paused = false;
        for (int i = 0; i < 60; i++) g.World.Tick();
        Assert.NotEqual(at, hornet.Position);
    }

    [Fact]
    public void RangedCreaturesNeedLineOfSight()
    {
        using var g = Create();
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        for (int y = 1; y <= 10; y++)
            for (int z = -6; z <= 6; z++)
                batch.SetBlock(g.Floor.X + 4, g.Floor.Y + y, g.Floor.Z + z, stone);
        g.World.Session.Commit(batch);
        Spawn(g, "fire_imp", TestGame.CellFeet(g.Floor + new Int3(8, 1, 0)));
        g.Ticks(400);
        Assert.DoesNotContain(g.World.Projectiles.All, p => !p.Def.Friendly);
    }

    [Fact]
    public void SwimmersStayInWaterAndFlopOnLand()
    {
        using var g = Create();
        var center = g.Floor + new Int3(6, 0, 6);
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
                for (int y = 1; y <= 4; y++)
                    batch.SetBlock(center.X + x, center.Y + y, center.Z + z, Math.Abs(x) == 3 || Math.Abs(z) == 3 ? stone : (ushort)0);
        g.World.Session.Commit(batch);
        g.World.Liquids.Enabled = false;
        g.World.Editor.FillLiquid(center + new Int3(-2, 1, -2), center + new Int3(2, 4, 2), g.Content.Liquids.Get("water"), LiquidCode.MaxLevel);
        var fish = Spawn(g, "shark", TestGame.CellFeet(center + new Int3(0, 2, 0)));
        var stranded = Spawn(g, "shark", TestGame.CellFeet(g.Floor + new Int3(-6, 4, -6)));
        g.Ticks(360);
        Assert.NotEqual(0, g.World.Session.World.GetLiquid(VoxelMotion.CellOf(fish.Center).X, VoxelMotion.CellOf(fish.Center).Y, VoxelMotion.CellOf(fish.Center).Z));
        Assert.True(stranded.OnGround || stranded.Position.Y < TestGame.CellFeet(g.Floor + new Int3(0, 2, 0)).Y);
    }

    [Fact]
    public void LungersReachOutButStayRooted()
    {
        using var g = Create();
        var root = TestGame.CellFeet(g.Floor + new Int3(6, 1, 0));
        var snatcher = Spawn(g, "man_eater", root);
        float reach = 7 * Units.CellSize;
        float closest = float.MaxValue;
        for (int i = 0; i < 300; i++)
        {
            g.World.Tick();
            Assert.True(Vector3.Distance(snatcher.Center, snatcher.Anchor + new Vector3(0, 0.6f, 0)) <= reach + 0.3f);
            closest = MathF.Min(closest, Vector3.Distance(snatcher.Center, g.Player.Center));
        }
        Assert.True(closest < Vector3.Distance(root, g.Player.Center) - 0.5f); // it lunged toward the player
    }

    [Fact]
    public void ContactDebuffsAndDeathEffects()
    {
        using var g = Create();
        var frost = Spawn(g, "ice_slime", g.Player.Position);
        for (int i = 0; i < 20 && !g.Player.State.Buffs.Has("chilled"); i++)
        {
            g.Player.State.Vitals.ImmunityTicks = 0;
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            frost.Position = g.Player.Position;
            g.World.Tick();
        }
        Assert.True(g.Player.State.Buffs.Has("chilled"));

        var cell = g.Floor + new Int3(-5, 1, -5);
        var magma = Spawn(g, "lava_slime", TestGame.CellFeet(cell));
        g.World.Npcs.Kill(magma);
        Assert.Equal((int)LiquidType.Lava, g.World.Session.World.GetLiquid(cell.X, cell.Y, cell.Z) >> 5);
    }
}
