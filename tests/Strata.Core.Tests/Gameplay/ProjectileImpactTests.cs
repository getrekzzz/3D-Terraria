using System.Numerics;
using Strata.Core.Content;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Projectiles striking the terrain (P3-11): one impact per strike, naming the block struck.</summary>
public class ProjectileImpactTests
{
    [Fact]
    public void AnArrowIntoAWallStrikesItOnce()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        // A stone wall 3 m ahead of the player, chest high.
        var batch = g.World.Session.World.BeginEdit();
        var stone = g.Content.Blocks.Get("stone").RuntimeId;
        for (int y = 1; y <= 6; y++)
            for (int x = -3; x <= 3; x++)
                batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + 6, (ushort)stone);
        g.World.Session.Commit(batch);
        var hits = new List<ushort>();
        g.World.Projectiles.HitTerrain += (_, _, block) => hits.Add(block);
        var start = new Vector3(g.Floor.X + 0.5f, g.Floor.Y + 3.5f, g.Floor.Z + 2.5f) * Strata.Core.Common.Units.CellSize;
        g.World.Projectiles.Spawn(g.Content.Projectiles.Get("wooden_arrow"), start, Vector3.UnitZ * 20f, g.Player, 5, 0, Strata.Core.Content.Defs.DamageClass.Ranged, 0);
        g.Ticks(60);
        Assert.Single(hits);
        Assert.Equal(stone, hits[0]);
        Assert.Empty(g.World.Projectiles.All);
    }

    [Fact]
    public void ABombAtRestDoesNotKeepStrikingTheGround()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        int hits = 0;
        g.World.Projectiles.HitTerrain += (_, _, _) => hits++;
        var start = new Vector3(g.Floor.X + 0.5f, g.Floor.Y + 3f, g.Floor.Z + 4.5f) * Strata.Core.Common.Units.CellSize;
        var bomb = g.World.Projectiles.Spawn(g.Content.Projectiles.Get("dynamite"), start, Vector3.Zero, g.Player, 0, 0, Strata.Core.Content.Defs.DamageClass.None, 0);
        g.Ticks(240);
        Assert.Contains(bomb, g.World.Projectiles.All); // its fuse, not its bounces, sets it off
        Assert.True(hits <= 6, $"{hits} impacts from a bomb lying still");
    }

    [Fact]
    public void AShotKnowsWhereItStartedWhenItAppears()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Hold("wooden_bow");
        g.Player.State.Inventory.Backpack[30] = new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("wooden_arrow"), 5);
        g.Player.Aim = Vector3.UnitZ;
        Vector3? seen = null;
        g.World.Projectiles.Spawned += p => seen = p.SpawnOrigin;
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.NotNull(seen);
        Assert.True(Vector3.Distance(seen!.Value, g.Player.Eye) < 2f, $"origin {seen} is not at the player");
    }

    [Fact]
    public void ShippedSpinsAndTrailsAreValid()
    {
        Assert.Equal("flat", TestPaths.Content.Projectiles.Get("shuriken").SpinAxis);
        Assert.Equal(22f, TestPaths.Content.Projectiles.Get("wooden_boomerang").Spin);
        Assert.NotNull(TestPaths.Content.Projectiles.Get("flaming_arrow").Trail);
        Assert.DoesNotContain(TestPaths.Content.Report.Errors, e => e.Message.Contains("spinAxis") || e.Message.Contains("trail"));
    }
}
