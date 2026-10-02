using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;
using Xunit;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P3-34: sleeping in a bed sets the spawn and, while nothing threatens the sleeper, night passes five times faster.</summary>
public class SleepTests
{
    private static (TestGame G, TileObjectInstance Bed) Bedroom(int rotation = 0)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Session.Clock.SetHour(22f);
        var bed = new TileObjectInstance(g.Content.TileObjects.Get("bed"), g.Floor + new Int3(3, 0, -2), rotation, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(bed));
        return (g, bed);
    }

    private static long Asleep(TestGame g, TileObjectInstance bed, int ticks)
    {
        Assert.Equal(InteractOutcome.Slept, g.World.Interaction.Interact(g.Player, bed));
        long start = g.World.Session.Clock.Tick;
        g.Ticks(ticks);
        return g.World.Session.Clock.Tick - start;
    }

    [Fact]
    public void SleepingSetsTheSpawnAndRunsTheClockFiveTimesAsFast()
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        long advanced = Asleep(g, bed, 600);
        Assert.True(advanced >= 2900, $"advanced {advanced}");
        Assert.True(g.Player.Sleeping);
        Assert.Equal(GameWorld.SleepTimeScale, g.World.TimeScale);
        Assert.True(g.Player.State.SpawnPoints.ContainsKey(g.World.Session.Settings.Name));
        // The body lies on the mattress, the head towards the headboard.
        var (feet, head) = GameWorld.BedRest(bed);
        Assert.Equal(feet, g.Player.Position);
        Assert.Equal(Vector3.UnitZ, head);
        // Interacting again gets up, standing at the bed; time is back to normal.
        Assert.Equal(InteractOutcome.Woke, g.World.Interaction.Interact(g.Player, bed));
        Assert.False(g.Player.Sleeping);
        g.Ticks(1);
        Assert.Equal(1, g.World.TimeScale);
        Assert.Equal(-Vector3.UnitX, GameWorld.BedRest(new TileObjectInstance(bed.Def, bed.Anchor, 1, 0)).Head);
    }

    [Theory]
    [InlineData("king_slime", 30f)]
    [InlineData("zombie", 9f)]
    public void ABossOrANearbyEnemyKeepsTimeAtItsNormalRate(string enemy, float distance)
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(enemy), g.Player.Position + new Vector3(-distance, 0, 0))!;
        npc.Invulnerable = true;
        g.Player.State.Vitals.Undying = true;
        Assert.Equal(600, Asleep(g, bed, 600));
    }

    [Fact]
    public void AnInvasionKeepsTimeAtItsNormalRate()
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        g.World.Events.Start(g.Content.Events.Get("pirate_invasion"));
        Assert.Equal(600, Asleep(g, bed, 600));
    }

    [Fact]
    public void GettingHurtOrLosingTheBedWakesTheSleeper()
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        Asleep(g, bed, 10);
        Assert.True(g.World.SleepSpeedsTime());
        Assert.True(g.Player.State.Vitals.Hurt(5));
        g.Ticks(1);
        Assert.False(g.Player.Sleeping);
        Assert.Equal(1, g.World.TimeScale);

        // Breaking the bed wakes a sleeper too.
        g.Ticks(5);
        Asleep(g, bed, 1);
        g.World.Session.World.TileObjects.Remove(bed);
        g.Ticks(1);
        Assert.False(g.Player.Sleeping);
    }

    [Fact]
    public void IntervalChecksStillComeRoundWhileTimeRunsFast()
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        Assert.Equal(InteractOutcome.Slept, g.World.Interaction.Interact(g.Player, bed));
        g.Ticks(10);
        // Once aligned, every tick the systems see is a multiple of the scale (so 60 and 600 still come round): the
        // extra ticks end each tick one short of the next multiple.
        for (int i = 0; i < 20; i++)
        {
            g.Ticks(1);
            Assert.Equal(GameWorld.SleepTimeScale - 1, g.World.Session.Clock.Tick % GameWorld.SleepTimeScale);
        }
    }

    [Fact]
    public void AFarCreatureStillTakesLavaContactDamageWhileTimeRunsFast()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Liquids.Enabled = false;
        g.World.Session.Clock.SetHour(22f);
        var bed = new TileObjectInstance(g.Content.TileObjects.Get("bed"), g.Floor + new Int3(9, 1, -2), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(bed));
        // A lava pool well past the threat radius (the arena ends at 12 cells; this is carved beyond it).
        var batch = g.World.Session.World.BeginEdit();
        var stone = g.Content.BlockId("stone");
        for (int z = -3; z <= 3; z++)
            for (int x = -27; x <= -21; x++)
            {
                batch.SetBlock(g.Floor.X + x, g.Floor.Y, g.Floor.Z + z, stone);
                for (int y = 1; y <= 8; y++) batch.SetBlock(g.Floor.X + x, g.Floor.Y + y, g.Floor.Z + z, 0);
            }
        g.World.Session.Commit(batch);
        g.World.Editor.FillLiquid(g.Floor + new Int3(-26, 1, -2), g.Floor + new Int3(-22, 7, 2), g.Content.Liquids.Get("lava"), Strata.Core.Voxels.LiquidCode.MaxLevel);
        // A creature whose id is not a multiple of the time scale: every tick the systems see is one while asleep.
        var at = TestGame.CellFeet(g.Floor + new Int3(-24, 1, 0));
        Strata.Core.Gameplay.Entities.Npc zombie;
        while ((zombie = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), at)!).Id % GameWorld.SleepTimeScale != 2)
            g.World.Npcs.Despawn(zombie);
        int life = zombie.Life;

        Assert.Equal(InteractOutcome.Slept, g.World.Interaction.Interact(g.Player, bed));
        g.Ticks(40);
        Assert.Equal(GameWorld.SleepTimeScale, g.World.TimeScale);
        Assert.True(!zombie.Alive || zombie.Life < life, "the far zombie took no lava damage");
    }

    [Fact]
    public void DamageOverTimeKeepsItsRealSecondWhileTimeRunsFast()
    {
        var (g, bed) = Bedroom();
        using var _ = g;
        Asleep(g, bed, 20);
        Assert.Equal(GameWorld.SleepTimeScale, g.World.TimeScale);
        Assert.True(g.World.Session.Clock.Tick > g.World.RealTick);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("poisoned"), 600);
        g.Player.State.Recompute();
        long start = g.World.RealTick;
        int life = g.Player.State.Vitals.Life;
        // The poison's hit lands on the next whole real second (and wakes the sleeper), not every fifth of one.
        long due = (start / 60 + 1) * 60;
        g.Ticks((int)(due - start) - 1);
        Assert.Equal(life, g.Player.State.Vitals.Life);
        Assert.True(g.Player.Sleeping);
        g.Ticks(2);
        Assert.True(g.Player.State.Vitals.Life < life);
        Assert.False(g.Player.Sleeping);
    }
}
