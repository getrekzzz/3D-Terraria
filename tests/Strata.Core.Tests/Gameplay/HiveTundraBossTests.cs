using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Queen Bee and Deerclops tuning: the jungle enrage, Expert volleys and the stomp.</summary>
public class HiveTundraBossTests
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

    private static void Fight(TestGame g, int ticks, Action? each = null)
    {
        for (int t = 0; t < ticks; t++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            each?.Invoke();
        }
    }

    [Fact]
    public void QueenBeeEnragesOutsideTheJungle()
    {
        using var g = Arena();
        var messages = new List<string>();
        g.World.Npcs.Messaged += (text, _) => messages.Add(text);
        var queen = Boss(g, "queen_bee", new Vector3(0, 4, 8));
        Assert.Equal(3400, queen.MaxLife);
        Fight(g, 60);
        Assert.True(queen.Frenzied, "the test arena is not jungle, so she should be enraged");
        Assert.Equal(1.5f, queen.SpeedScale);
        Assert.Single(messages, m => m == "Queen Bee is enraged!");
    }

    [Fact]
    public void ExpertQueenBeeFiresDenserStingerVolleys()
    {
        var densest = new Dictionary<Difficulty, int>();
        foreach (var difficulty in new[] { Difficulty.Classic, Difficulty.Expert })
        {
            using var g = Arena(difficulty);
            int thisTick = 0, most = 0;
            g.World.Projectiles.Spawned += p => { if (p.Def.Id == "queen_stinger") most = Math.Max(most, ++thisTick); };
            Boss(g, "queen_bee", new Vector3(0, 4, 8));
            Fight(g, 700, () => thisTick = 0);
            densest[difficulty] = most;
        }
        Assert.Equal(1, densest[Difficulty.Classic]);
        Assert.Equal(3, densest[Difficulty.Expert]);
    }

    [Fact]
    public void DeerclopsStompRaisesAnImpact()
    {
        using var g = Arena();
        var impacts = new List<Vector3>();
        g.World.Npcs.Impacted += (at, _) => impacts.Add(at);
        var deer = Boss(g, "deerclops", new Vector3(0, 0.2f, 5));
        Assert.Equal(7000, deer.MaxLife);
        bool shards = false;
        Fight(g, 600, () => shards |= g.World.Projectiles.All.Any(p => p.Def.Id == "deerclops_ice"));
        Assert.NotEmpty(impacts);
        Assert.True(shards);
        Assert.DoesNotContain(g.World.Projectiles.All, p => p.Def.Id == "deerclops_shadow_hand"); // Expert-only in its first phase
    }

    [Fact]
    public void DeerclopsStompsWhereItStands()
    {
        // Its stomp is a wind-up in place, not a 14 m/s glide onto the player (the slam's default tracking).
        using var g = Arena();
        var impacts = new List<Vector3>();
        g.World.Npcs.Impacted += (at, _) => impacts.Add(at);
        var deer = Boss(g, "deerclops", new Vector3(0, 0.2f, 10));
        g.World.Tick();
        deer.Counter = 2; // the stomp
        deer.Timer = 0;
        var start = deer.Position;
        float fastest = 0, moved = 0;
        Fight(g, 50, () =>
        {
            if (deer.Counter != 2) return;
            if (deer.Timer > 10) fastest = MathF.Max(fastest, new Vector2(deer.Velocity.X, deer.Velocity.Z).Length());
            moved = new Vector2(deer.Position.X - start.X, deer.Position.Z - start.Z).Length();
        });
        Assert.True(fastest < 1f, $"slid at {fastest:0.0} m/s during the stomp");
        Assert.True(moved < 1f, $"moved {moved:0.00} m during the stomp");
        Assert.Single(impacts);
    }
}
