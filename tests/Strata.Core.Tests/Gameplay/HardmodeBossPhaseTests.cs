using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P4-26 / P4-32: Queen Slime's flight phase; Duke Fishron's third phase and ocean rage; the Empress by day.</summary>
public class HardmodeBossPhaseTests
{
    private static TestGame Arena(float hour = 22f)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        g.World.Session.Clock.SetHour(hour);
        return g;
    }

    private static Npc Boss(TestGame g, string id, Vector3 offset) => g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + offset)!;

    private static void Run(TestGame g, int ticks, Func<bool>? until = null)
    {
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        }
    }

    [Fact]
    public void QueenSlimeTakesWingAtHalfLife()
    {
        using var g = Arena();
        var queen = Boss(g, "queen_slime", new Vector3(0, 2f, 10f));
        var brain = g.World.Npcs.BrainOf(queen.Def);
        Run(g, 30);
        Assert.Equal(0, queen.Stage);
        Assert.True(brain.UsesGravityFor(queen), "she hops on the ground at first");

        queen.Life = (int)(queen.MaxLife * 0.45f);
        Run(g, 100);
        Assert.Equal(1, queen.Stage);
        Assert.Equal("queen_slime_winged", queen.ModelOverride);
        Assert.False(brain.UsesGravityFor(queen));
        Assert.True(queen.Position.Y - g.Player.Position.Y >= 5f, $"hovering {queen.Position.Y - g.Player.Position.Y:F1} m above");
    }

    [Fact]
    public void QueenSlimeSlamsWithAnImpact()
    {
        using var g = Arena();
        var queen = Boss(g, "queen_slime", new Vector3(0, 2f, 10f));
        queen.Life = (int)(queen.MaxLife * 0.45f);
        var impacts = new List<Vector3>();
        g.World.Npcs.Impacted += (at, _) => impacts.Add(at);
        Run(g, 600, () => impacts.Count > 0 && queen.Stage == 1);
        Assert.NotEmpty(impacts);
        Assert.Contains(g.World.Projectiles.All, p => p.Def.Id == "queen_slime_crystal");
    }

    [Fact]
    public void QueenSlimeSummonsHerCourt()
    {
        using var g = Arena();
        var queen = Boss(g, "queen_slime", new Vector3(0, 2f, 10f));
        var kinds = new HashSet<string>();
        g.World.Npcs.Spawned += n => kinds.Add(n.Def.Id);
        Run(g, 1500);
        Assert.Contains(kinds, k => k is "crystal_slime" or "bouncy_slime" or "heavenly_slime");
        Assert.True(kinds.Count(k => k is "crystal_slime" or "bouncy_slime" or "heavenly_slime") >= 2, string.Join(",", kinds));
    }
}
