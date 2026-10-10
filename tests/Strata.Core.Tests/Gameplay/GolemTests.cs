using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P4-31: Golem's sealed body, its head that breaks loose and flies free, and its punching stone fists.</summary>
public class GolemTests
{
    private static (TestGame g, Npc golem) Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        var golem = g.World.Npcs.Spawn(g.Content.Enemies.Get("golem"), g.Player.Position + new Vector3(0, 0.5f, 10f))!;
        return (g, golem);
    }

    private static Npc? Single(TestGame g, string id) => g.World.Npcs.All.SingleOrDefault(n => n.Def.Id == id && n.Alive);

    private static bool Strike(TestGame g, Npc npc) =>
        g.World.Npcs.Hit(npc, g.Player, 500, DamageClass.Melee, 0, 0f, g.Player.Center, false);

    [Fact]
    public void BodyIsSealedUntilItsHeadFalls()
    {
        var (g, golem) = Arena();
        using var scope = g;
        Assert.Equal((int)(39000 * g.World.Npcs.Scale), golem.MaxLife);
        var head = Single(g, "golem_head")!;
        Assert.True(head.IsPart);
        Assert.Equal((int)(16000 * g.World.Npcs.Scale), head.MaxLife);
        Assert.True(head.Center.Y > golem.Center.Y + 2f, "the head sits on top of the body");
        g.Ticks(3);
        Assert.True(golem.Invulnerable, $"stage {golem.Stage} age {golem.Age} life {golem.Life} pos {golem.Position} player {g.Player.Position}");
        Assert.False(Strike(g, golem));
        Assert.True(Strike(g, head), "the head can be struck while it lives");
        g.World.Npcs.Kill(head);
        g.Ticks(3);
        Assert.False(golem.Invulnerable);
        Assert.Equal(1, golem.Stage);
        Assert.True(Strike(g, golem));
        Assert.False(g.World.Progression.IsSet("defeated_golem"));
    }

    [Fact]
    public void TheFallenHeadFliesFreeUntilTheBodyDies()
    {
        var (g, golem) = Arena();
        using var scope = g;
        g.Ticks(2);
        g.World.Npcs.Kill(Single(g, "golem_head")!);
        var free = Single(g, "golem_head_free")!;
        Assert.Same(golem, free.BoundTo);
        Assert.Null(free.Leader);
        Assert.True(free.Invulnerable);
        Assert.False(Strike(g, free));
        var start = free.Center;
        bool fired = false;
        for (int i = 0; i < 300; i++)
        {
            g.World.Tick();
            fired |= g.World.Projectiles.All.Any(p => p.Owner == null && Vector3.Distance(p.Position, free.Center) < 4f);
        }
        Assert.True(free.Alive);
        Assert.True(Vector3.Distance(free.Center, start) > 2f, "the freed head flies on its own");
        Assert.True(fired, "the freed head keeps firing");
        g.World.Npcs.Kill(golem);
        g.Ticks(2);
        Assert.Null(Single(g, "golem_head_free"));
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "golem_head_free");
    }

    [Fact]
    public void FistsPunchAtTheTargetAndCanBeBroken()
    {
        var (g, golem) = Arena();
        using var scope = g;
        var fists = golem.Followers.Where(f => f.Def.Id == "golem_fist").ToList();
        Assert.Equal(2, fists.Count);
        Assert.All(fists, f => Assert.Equal((int)(9000 * g.World.Npcs.Scale), f.MaxLife));
        Assert.Single(fists, f => f.Mirrored);
        float nearest = float.MaxValue;
        bool held = false;
        for (int i = 0; i < 400; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            foreach (var f in fists)
            {
                nearest = MathF.Min(nearest, Vector3.Distance(f.Center, g.Player.Center));
                held |= f.Phase == 4;
            }
        }
        Assert.True(nearest < 3f, $"a fist punched out to the player (closest {nearest:F1} m)");
        Assert.True(held, "a punch holds before it retracts");
        g.World.Npcs.Kill(fists[0]);
        g.Ticks(2);
        Assert.True(golem.Alive);
        Assert.Single(g.World.Npcs.All, n => n.Def.Id == "golem_fist" && n.Alive);
        Assert.False(g.World.Progression.IsSet("defeated_golem"));
    }

    [Fact]
    public void SlayingTheBodyEndsTheFight()
    {
        var (g, golem) = Arena();
        using var scope = g;
        int defeats = 0;
        g.World.Npcs.BossDefeated += _ => defeats++;
        g.Ticks(2);
        g.World.Npcs.Kill(Single(g, "golem_head")!);
        Assert.Equal(0, defeats);
        g.Ticks(2);
        g.World.Npcs.Kill(golem);
        g.Ticks(2);
        Assert.Equal(1, defeats);
        Assert.True(g.World.Progression.IsSet("defeated_golem"));
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id.StartsWith("golem"));
        // Slain outright with its head still on: the parts fall with it, no head breaks free, one defeat.
        var (g2, golem2) = Arena();
        using var scope2 = g2;
        int defeats2 = 0;
        g2.World.Npcs.BossDefeated += _ => defeats2++;
        g2.Ticks(2);
        g2.World.Npcs.Kill(golem2);
        g2.Ticks(2);
        Assert.Equal(1, defeats2);
        Assert.DoesNotContain(g2.World.Npcs.All, n => n.Def.Id.StartsWith("golem"));
    }
}
