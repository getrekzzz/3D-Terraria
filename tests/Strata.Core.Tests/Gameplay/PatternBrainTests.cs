using System.Numerics;
using System.Text.Json;
using Strata.Core.AI;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Pattern-brain features: Expert overrides, flight, slams, leashes, auras and illusions.</summary>
public class PatternBrainTests
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

    /// <summary>A test boss whose brain is the pattern JSON given (single quotes for readability).</summary>
    private static EnemyDef Boss(string brain, bool ground = false, string id = "test_pattern") => new()
    {
        Id = id,
        Life = 5000,
        Size = new[] { 2f, 2f },
        ContactDamage = false,
        Gravity = ground,
        CollideTerrain = ground,
        Brain = new TypedSpec("pattern", JsonDocument.Parse(brain.Replace('\'', '"')).RootElement.Clone()),
    };

    private static Npc Spawn(TestGame g, EnemyDef def, Vector3 offset) => g.World.Npcs.Spawn(def, g.Player.Position + offset)!;

    [Fact]
    public void ExpertOnlyMovesAreSkippedInClassic()
    {
        var def = Boss("{'type':'pattern','phases':[{'moves':[{'type':'wait','ticks':20,'pose':'classic'},{'type':'wait','ticks':20,'pose':'expert','expertOnly':true}]}]}");
        foreach (var difficulty in new[] { Difficulty.Classic, Difficulty.Expert })
        {
            using var g = Arena(difficulty);
            var npc = Spawn(g, def, new Vector3(0, 2, 4));
            var poses = new HashSet<string>();
            for (int t = 0; t < 120; t++)
            {
                g.World.Tick();
                poses.Add(npc.Pose);
            }
            Assert.Contains("classic", poses);
            Assert.Equal(difficulty == Difficulty.Expert, poses.Contains("expert"));
        }
    }

    [Fact]
    public void ExpertOverridesSpeedUpAndThickenVolleys()
    {
        var def = Boss("{'type':'pattern','phases':[{'expert':{'speed':1.5,'volleyCount':3},"
            + "'volley':{'projectile':'queen_stinger','interval':30,'count':2,'speed':10},"
            + "'moves':[{'type':'chase','ticks':600,'speed':4}]}]}");
        var shots = new int[2];
        var top = new float[2];
        foreach (var (difficulty, k) in new[] { (Difficulty.Classic, 0), (Difficulty.Expert, 1) })
        {
            using var g = Arena(difficulty);
            var npc = Spawn(g, def, new Vector3(0, 2, 5));
            g.Ticks(30);
            shots[k] = g.World.Projectiles.All.Count(p => p.Def.Id == "queen_stinger");
            for (int t = 0; t < 30; t++)
            {
                g.World.Tick();
                top[k] = MathF.Max(top[k], npc.Velocity.Length());
            }
        }
        Assert.Equal(2, shots[0]);
        Assert.Equal(5, shots[1]);
        Assert.True(top[0] <= 4.05f, $"classic top speed {top[0]:0.00}");
        Assert.True(top[1] > 4.5f, $"expert top speed {top[1]:0.00}");
    }

    [Fact]
    public void FlyPhaseSuspendsGravity()
    {
        using var g = Arena();
        const string moves = "'moves':[{'type':'hover','ticks':600,'speed':6,'height':3,'distance':3}]";
        var flier = Spawn(g, Boss("{'type':'pattern','ground':true,'phases':[{'fly':true," + moves + "}]}", ground: true), new Vector3(3, 0.1f, 0));
        var walker = Spawn(g, Boss("{'type':'pattern','ground':true,'phases':[{'moves':[{'type':'wait','ticks':600}]}]}", ground: true, id: "test_walker"), new Vector3(-3, 0.5f, 0));
        float floor = g.Player.Position.Y;
        g.Ticks(60);
        for (int t = 0; t < 120; t++)
        {
            g.World.Tick();
            Assert.True(flier.Position.Y > floor + 1.5f, $"the flier sank to {flier.Position.Y - floor:0.00} m");
        }
        Assert.True(walker.Position.Y < floor + 0.5f, "gravity no longer holds a ground boss outside a fly phase");
    }

    [Fact]
    public void SlamLandsOnTheTargetAndRaisesAnImpact()
    {
        using var g = Arena();
        var def = Boss("{'type':'pattern','ground':true,'phases':[{'moves':[{'type':'slam','height':4,'telegraph':50,'speed':16,'ticks':90,"
            + "'volley':{'projectile':'deerclops_ice','count':6,'ring':true,'interval':999}},{'type':'wait','ticks':600}]}]}", ground: true);
        var npc = Spawn(g, def, new Vector3(4, 0.1f, 0));
        var impacts = new List<Vector3>();
        g.World.Npcs.Impacted += (at, strength) => impacts.Add(at);
        int shards = 0;
        for (int t = 0; t < 160; t++)
        {
            g.World.Tick();
            shards = Math.Max(shards, g.World.Projectiles.All.Count(p => p.Def.Id == "deerclops_ice"));
        }
        var hit = Assert.Single(impacts);
        var flat = new Vector2(hit.X - g.Player.Position.X, hit.Z - g.Player.Position.Z);
        Assert.True(flat.Length() < 2f, $"landed {flat.Length():0.00} m from the target");
        Assert.True(npc.OnGround);
        Assert.Equal(6, shards);
    }

    [Fact]
    public void LeashHoldsTheBossNearItsAnchors()
    {
        using var g = Arena();
        var def = Boss("{'type':'pattern','phases':[{'summon':{'enemy':'tesla_turret','once':true,'max':3,'tether':true,'radius':1.5},"
            + "'leash':{'kind':'tesla_turret','radius':2},'moves':[{'type':'chase','ticks':600,'speed':10}]}]}");
        var npc = Spawn(g, def, new Vector3(5, 1, 0));
        g.World.Tick();
        var anchors = g.World.Npcs.All.Where(n => n.Def.Id == "tesla_turret").ToList();
        Assert.Equal(3, anchors.Count);
        Assert.All(anchors, a => Assert.Same(npc, a.Leader));
        for (int t = 0; t < 120; t++)
        {
            g.World.Tick();
            var centroid = anchors.Aggregate(Vector3.Zero, (s, a) => s + a.Center) / anchors.Count;
            Assert.True(Vector3.Distance(npc.Center, centroid) < 2.6f, $"strayed {Vector3.Distance(npc.Center, centroid):0.00} m");
        }
        Assert.True(Vector3.Distance(npc.Center, g.Player.Center) > 2f);
        g.World.Npcs.Kill(npc);
        g.Ticks(2);
        Assert.DoesNotContain(g.World.Npcs.All, n => n.Def.Id == "tesla_turret"); // tethered: they die with the boss
    }

    [Theory]
    [InlineData(3f, false, Difficulty.Classic, true)]
    [InlineData(5.5f, false, Difficulty.Classic, false)]
    [InlineData(3f, true, Difficulty.Classic, false)]
    [InlineData(3f, true, Difficulty.Expert, true)]
    public void AuraPulsesItsBuffOnlyInRange(float distance, bool expertOnly, Difficulty difficulty, bool confused)
    {
        using var g = Arena(difficulty);
        var def = Boss("{'type':'pattern','phases':[{'aura':{'buff':'confused','ticks':120,'interval':30,'radius':4,'expertOnly':" + (expertOnly ? "true" : "false") + "},"
            + "'moves':[{'type':'wait','ticks':600}]}]}");
        Spawn(g, def, new Vector3(0, 0.1f, distance));
        g.Ticks(29);
        Assert.False(g.Player.State.Buffs.Has("confused"));
        g.Ticks(2);
        Assert.Equal(confused, g.Player.State.Buffs.Has("confused"));
    }

    [Fact]
    public void IllusionsMirrorAroundTheTarget()
    {
        using var g = Arena();
        var npc = Spawn(g, Boss("{'type':'pattern','phases':[{'illusions':3,'moves':[{'type':'wait','ticks':600}]}]}"), new Vector3(3, 2, 1));
        g.Ticks(2);
        Assert.Equal(3, npc.Illusions);
        var m = npc.MirrorAround;
        Assert.Equal(g.Player.Center, m);
        var real = npc.Center - m;
        var seen = new List<Vector3> { real };
        for (int i = 0; i < npc.Illusions; i++)
        {
            var o = Illusions.Position(npc, i) - m;
            Assert.Equal(real.Length(), o.Length(), 3);
            Assert.Equal(real.Y, o.Y, 3);
            seen.Add(o);
        }
        // The real body and its three copies stand a quarter turn apart around the target.
        for (int i = 0; i < 4; i++)
        {
            var a = seen[i];
            var b = seen[(i + 1) % 4];
            Assert.Equal(0f, a.X * b.X + a.Z * b.Z, 3);
        }
        Assert.Equal(-real.X, seen[2].X, 3);
        Assert.Equal(-real.Z, seen[2].Z, 3);
    }
}
