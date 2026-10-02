using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>King Slime and the Eye of Cthulhu as in Terraria: shrinking, sight teleports, Expert minions; the Eye's tear-open.</summary>
public class KingSlimeEyeTests
{
    private static TestGame Arena(Difficulty difficulty = Difficulty.Classic)
    {
        var g = TestGame.Create(difficulty: difficulty);
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static Npc Boss(TestGame g, string id, Vector3 offset) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + offset)!;

    [Fact]
    public void KingSlimeShrinksAsItLosesLife()
    {
        using var g = Arena();
        var boss = Boss(g, "king_slime", new Vector3(0, 0.5f, 4f));
        g.Ticks(2);
        Assert.Equal(1f, boss.VisualScale, 3);
        Assert.Equal(2000, boss.MaxLife);
        boss.Life = boss.MaxLife / 2;
        g.Ticks(2);
        Assert.Equal(0.8f, boss.VisualScale, 2);
        Assert.Equal(boss.BaseHalfExtents.Y * 0.8f, boss.HalfExtents.Y, 2);
        boss.Life = 1;
        g.Ticks(2);
        Assert.InRange(boss.VisualScale, 0.6f, 0.61f);
    }

    [Fact]
    public void KingSlimeTeleportsToATargetItCannotSee()
    {
        using var g = Arena();
        // A stone wall 2 m in front of the player, from wall to wall of the arena and well above it.
        var stone = g.Content.BlockId("stone");
        var batch = g.World.Session.World.BeginEdit();
        for (int dx = -TestGame.ArenaRadius; dx <= TestGame.ArenaRadius; dx++)
            for (int dy = 1; dy <= 30; dy++)
                for (int dz = 4; dz <= 5; dz++)
                    batch.SetBlock(g.Floor.X + dx, g.Floor.Y + dy, g.Floor.Z + dz, stone);
        g.World.Session.Commit(batch);
        var boss = Boss(g, "king_slime", new Vector3(0, 0.2f, 4.6f));
        float wall = TestGame.CellFeet(g.Floor + new Int3(0, 0, 4)).Z - 0.25f;
        Assert.True(boss.Position.Z > wall);
        int ticks = 0;
        while (boss.Position.Z > wall && ticks < 300 + 50 + 60)
        {
            g.World.Tick();
            ticks++;
        }
        Assert.True(boss.Position.Z < wall, $"still behind the wall after {ticks} ticks");
        Assert.True(ticks > 300, $"teleported after {ticks} ticks, before losing sight long enough");
    }

    [Fact]
    public void KingSlimeShedsSpikedSlimesInExpert()
    {
        foreach (var (difficulty, spiked) in new[] { (Difficulty.Classic, false), (Difficulty.Expert, true) })
        {
            using var g = Arena(difficulty);
            var boss = Boss(g, "king_slime", new Vector3(0, 0.5f, 4f));
            g.Ticks(2);
            boss.Life = (int)(boss.MaxLife * 0.4f);
            g.Ticks(2);
            Assert.True(g.World.Npcs.All.Count(n => n.Def.Id == "green_slime") >= 5);
            Assert.Equal(spiked, g.World.Npcs.All.Any(n => n.Def.Id == "spiked_slime"));
        }
    }

    [Fact]
    public void KingSlimeLandingsRaiseImpacts()
    {
        using var g = Arena();
        var impacts = 0;
        g.World.Npcs.Impacted += (_, _) => impacts++;
        Boss(g, "king_slime", new Vector3(0, 0.5f, 4f));
        g.Ticks(400);
        Assert.True(impacts >= 2, $"{impacts} impacts");
    }

    [Fact]
    public void EyeLosesDefenseAndBitesHarderInPhaseTwo()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(22f);
        var eye = Boss(g, "eye_of_cthulhu", new Vector3(0, 4f, 6f));
        g.Ticks(2);
        Assert.Equal(12, eye.Defense);
        Assert.Equal(2800, eye.MaxLife);
        eye.Life = (int)(eye.MaxLife * 0.45f);
        g.Ticks(2);
        Assert.True(eye.Invulnerable); // tearing open
        Assert.Equal("transform", eye.Pose);
        Assert.Equal("eye_of_cthulhu_mouth", eye.ModelOverride);
        g.Ticks(62);
        Assert.False(eye.Invulnerable);
        Assert.Equal(0, eye.Defense);
        Assert.Equal(23, (int)MathF.Round(eye.Def.Damage * eye.DamageScale));
    }

    [Fact]
    public void ExpertEyeChainsMoreDashesWhenLow()
    {
        var most = new int[2];
        foreach (var (difficulty, k) in new[] { (Difficulty.Classic, 0), (Difficulty.Expert, 1) })
        {
            using var g = Arena(difficulty);
            g.World.Session.Clock.SetHour(22f);
            var eye = Boss(g, "eye_of_cthulhu", new Vector3(0, 4f, 6f));
            g.Ticks(2);
            eye.Life = (int)(eye.MaxLife * 0.25f);
            for (int t = 0; t < 1200; t++)
            {
                g.World.Tick();
                g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
                most[k] = Math.Max(most[k], eye.Counter);
            }
            Assert.True(eye.Alive);
        }
        Assert.Equal(3, most[0]);
        Assert.Equal(5, most[1]);
    }
}
