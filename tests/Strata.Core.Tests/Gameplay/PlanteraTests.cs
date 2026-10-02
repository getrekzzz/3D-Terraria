using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Physics;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Plantera as in Terraria: hooks that latch onto blocks and leash her, tentacles in phase two, the jungle enrage.</summary>
public class PlanteraTests
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

    private static Npc Plantera(TestGame g) => g.World.Npcs.Spawn(g.Content.Enemies.Get("plantera"), g.Player.Position + new Vector3(0, 3f, 6f))!;

    private static List<Npc> Living(TestGame g, string id) => g.World.Npcs.All.Where(n => n.Alive && !n.Removed && n.Def.Id == id).ToList();

    private static void Fight(TestGame g, int ticks, Action? each = null)
    {
        for (int t = 0; t < ticks; t++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            each?.Invoke();
        }
    }

    private static bool NextToSolid(TestGame g, Vector3 p)
    {
        var c = VoxelMotion.CellOf(p);
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (g.World.Session.World.IsSolid(c.X + dx, c.Y + dy, c.Z + dz)) return true;
        return false;
    }

    [Fact]
    public void HooksLatchOntoSolidBlocksNearTheTarget()
    {
        using var g = Arena();
        var plantera = Plantera(g);
        Assert.Equal(30_000, plantera.MaxLife);
        Fight(g, 150);
        var hooks = Living(g, "plantera_hook");
        Assert.Equal(3, hooks.Count);
        foreach (var hook in hooks)
        {
            Assert.Same(plantera, hook.Leader);
            Assert.True(hook.Invulnerable);
            Assert.Equal(1, hook.Phase); // latched
            Assert.True(NextToSolid(g, hook.Direction), $"hook latched at {hook.Direction} in the air");
            Assert.True(Vector3.Distance(hook.Center, hook.Direction) < 0.5f);
            Assert.True(Vector3.Distance(hook.Direction, g.Player.Center) <= 14.5f, "within reach of the target");
        }
        Assert.False(g.World.Npcs.Hit(hooks[0], g.Player, 500, Strata.Core.Content.Defs.DamageClass.Melee, 0, 0f, g.Player.Center, melee: true));
    }

    [Fact]
    public void PlanteraStaysWithinHerLeash()
    {
        using var g = Arena();
        var plantera = Plantera(g);
        float worst = 0f;
        Fight(g, 600, () =>
        {
            var hooks = Living(g, "plantera_hook");
            if (hooks.Count == 0) return;
            var centroid = hooks.Aggregate(Vector3.Zero, (s, h) => s + h.Center) / hooks.Count;
            worst = MathF.Max(worst, Vector3.Distance(plantera.Center, centroid));
        });
        Assert.True(worst <= 9.6f, $"strayed {worst:F1} m from her hooks");
    }

    [Fact]
    public void TentaclesSproutInPhaseTwoAndDieWithHer()
    {
        foreach (var (difficulty, count) in new[] { (Difficulty.Classic, 8), (Difficulty.Expert, 12) })
        {
            using var g = Arena(difficulty);
            var plantera = Plantera(g);
            Fight(g, 5);
            Assert.Empty(Living(g, "plantera_tentacle"));
            plantera.Life = plantera.MaxLife * 2 / 5;
            Fight(g, 5);
            Assert.Equal("plantera_open", plantera.ModelOverride);
            var tentacles = Living(g, "plantera_tentacle");
            Assert.Equal(count, tentacles.Count);
            Assert.All(tentacles, t => Assert.Same(plantera, t.Leader));
            Assert.Equal(3, Living(g, "plantera_hook").Count); // her hooks hold on
            g.World.Npcs.Kill(plantera);
            Fight(g, 2);
            Assert.Empty(Living(g, "plantera_tentacle"));
            Assert.Empty(Living(g, "plantera_hook"));
        }
    }

    [Fact]
    public void PlanteraEnragesOutsideTheUndergroundJungle()
    {
        using var g = Arena();
        var plantera = Plantera(g);
        Fight(g, 61);
        Assert.True(plantera.Frenzied, "the test arena is a forest surface");
        Assert.Equal(1.6f, plantera.SpeedScale);

        // Somewhere deep under the jungle she calms down.
        var session = g.World.Session;
        Int3? jungle = null;
        for (int x = -2000; x <= 2000 && jungle == null; x += 24)
            for (int z = -2000; z <= 2000 && jungle == null; z += 24)
            {
                int surface = (int)session.Generator.Terrain.SurfaceHeight(x, z);
                var cell = new Int3(x, surface - 60, z);
                if (session.ContextAt(cell).Biomes.Contains("underground_jungle")) jungle = cell;
            }
        Assert.True(jungle.HasValue, "the test world has an underground jungle");
        var feet = TestGame.CellFeet(jungle!.Value);
        Fight(g, 61, () =>
        {
            g.Player.Position = feet;
            g.Player.Velocity = Vector3.Zero;
            plantera.Position = feet + new Vector3(0, 3f, 6f);
        });
        Assert.False(plantera.Frenzied);
        Assert.Equal(1f, plantera.SpeedScale);
    }
}
