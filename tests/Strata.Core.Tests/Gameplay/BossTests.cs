using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class BossTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000; // survive long fights (bosses leave when every player is dead)
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static Npc Boss(TestGame g, string id, Vector3 offset) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + offset)!;

    [Fact]
    public void SummonItemsRespectConditions()
    {
        using var g = Arena();
        var refused = new List<string>();
        g.World.ItemUse.Refused += (_, m) => refused.Add(m);
        g.World.Session.Clock.SetHour(12f);
        g.Hold("suspicious_looking_eye", 2);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Single(refused);
        g.World.Session.Clock.SetHour(22f);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Contains(g.World.Npcs.All, n => n.Def.Id == "eye_of_cthulhu");
        g.Ticks(60);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true)); // one at a time
    }

    [Fact]
    public void GelSovereignShedsMinionsAndTeleportsToFleeingPlayers()
    {
        using var g = Arena();
        var boss = Boss(g, "king_slime", new Vector3(0, 0.5f, 6f));
        g.Ticks(10);
        int before = g.World.Npcs.All.Count;
        boss.Life -= boss.MaxLife / 4;
        g.Ticks(2);
        Assert.True(g.World.Npcs.All.Count >= before + 3, "no minions shed");

        g.Player.Position += new Vector3(0, 0, -40f);
        g.Ticks(60 * 6);
        Assert.True(Vector3.Distance(boss.Position, g.Player.Position) < 15f, "boss did not follow the player");
    }

    [Fact]
    public void WatcherDashesTransformsAndLeavesAtDawn()
    {
        using var g = Arena();
        g.World.Session.Clock.SetHour(22f);
        var boss = Boss(g, "eye_of_cthulhu", new Vector3(0, 6f, 8f));
        var gone = new List<string>();
        g.World.Npcs.Removed += n => gone.Add($"{n.Def.Id}@{g.World.Session.Clock.Tick} life={n.Life} far={n.FarTicks} rel={n.Center - g.Player.Position}");
        float fastest = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            g.World.Tick();
            fastest = MathF.Max(fastest, boss.Velocity.Length());
        }
        Assert.True(fastest > 12f, $"never dashed (max speed {fastest:0.0})");
        Assert.True(g.World.Npcs.All.Any(n => n.Def.Id == "servant_of_cthulhu"), string.Join("; ", gone));
        boss.Life = boss.MaxLife / 3;
        g.Ticks(2);
        Assert.Equal("eye_of_cthulhu_mouth", boss.ModelOverride);

        var left = new List<Npc>();
        g.World.Npcs.BossLeft += left.Add;
        g.World.Session.Clock.SetHour(12f);
        g.Ticks(5);
        Assert.Contains(boss, left);
    }

    [Fact]
    public void BoreTyrantTunnelsAndItsSegmentsCanBeHit()
    {
        using var g = Arena();
        var world = g.World.Session.World;
        var boss = Boss(g, "eater_of_worlds", new Vector3(0, -0.6f, 8f)); // inside the arena's stone floor
        var trace = new System.Text.StringBuilder();
        for (int i = 0; i < 60 * 3; i++)
        {
            g.World.Tick();
            if (i % 20 == 0) trace.Append($"[{i}: rel={boss.Center - g.Player.Position} vel={boss.Velocity} target={boss.TargetPlayer} solid={g.World.Session.World.IsSolid(Strata.Core.Physics.VoxelMotion.CellOf(boss.Center).X, Strata.Core.Physics.VoxelMotion.CellOf(boss.Center).Y, Strata.Core.Physics.VoxelMotion.CellOf(boss.Center).Z)}] ");
        }
        Assert.Equal(18, boss.Segments.Count);
        for (int i = 1; i < boss.Segments.Count; i++)
            Assert.InRange(Vector3.Distance(boss.Segments[i], boss.Segments[i - 1]), 0.5f, 1.2f);

        // The floor under the arena has been chewed.
        int open = 0;
        for (int x = -12; x <= 12; x++)
            for (int z = -12; z <= 12; z++)
                for (int y = -1; y <= 0; y++)
                    if (world.GetBlock(g.Floor + new Strata.Core.Common.Int3(x, y, z)) == 0) open++;
        Assert.True(open > 0, "the worm did not dig " + trace);

        // A projectile at a body segment damages the shared life pool (fresh worm inside the loaded arena).
        g.World.Npcs.Clear();
        boss = Boss(g, "eater_of_worlds", new Vector3(0, 5f, 6f));
        int life = boss.Life;
        var seg = boss.Segments[1];
        g.World.Projectiles.Spawn(g.Content.Projectiles.Get("wooden_arrow"), seg + new Vector3(0, 0, -0.1f), Vector3.Zero, g.Player, 20, 1, Strata.Core.Content.Defs.DamageClass.Ranged, 0);
        g.World.Projectiles.Tick();
        Assert.True(boss.Life < life);
    }

    [Fact]
    public void DefeatingABossSetsItsFlagAndDropsLoot()
    {
        using var g = Arena();
        var boss = Boss(g, "king_slime", new Vector3(0, 0.5f, 6f));
        var defeated = new List<Npc>();
        g.World.Npcs.BossDefeated += defeated.Add;
        g.World.Npcs.Kill(boss);
        Assert.Single(defeated);
        Assert.Contains("defeated_king_slime", g.World.Session.Flags);
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "gel" && d.Stack.Count >= 1);
    }

    [Fact]
    public void JourneyWorldsDropNoExpertItems()
    {
        using var g = TestGame.Create(difficulty: Strata.Core.World.Difficulty.Journey);
        g.World.Spawner.Enabled = false;
        var def = g.Content.Enemies.Get("king_slime");
        for (int i = 0; i < 50; i++)
        {
            var boss = g.World.Npcs.Spawn(def, g.Player.Position + new Vector3(0, 0.5f, 6f))!;
            g.World.Npcs.Kill(boss);
            foreach (var slime in g.World.Npcs.All.ToList()) g.World.Npcs.Despawn(slime);
        }
        Assert.DoesNotContain(g.World.Drops.Items, d => d.Stack.Item?.Id == "royal_gel");
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item?.Id == "gel");
    }
}
