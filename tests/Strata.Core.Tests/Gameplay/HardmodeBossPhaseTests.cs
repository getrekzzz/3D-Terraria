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

    [Fact]
    public void DukeEnragesOutOfTheOcean()
    {
        using var g = Arena();
        var messages = new List<string>();
        g.World.Npcs.Announced += messages.Add;
        var duke = Boss(g, "duke_fishron", new Vector3(0, 6f, 12f));
        Run(g, 35);
        Assert.True(duke.Frenzied, "the forest is not the ocean");
        Assert.Equal(100, duke.Defense);
        Assert.Equal(1.5f, duke.SpeedScale);
        Assert.Contains("Duke Fishron is enraged!", messages);
    }

    [Fact]
    public void DukeEntersPhaseThreeBelowFifteenPercent()
    {
        using var g = Arena();
        var duke = Boss(g, "duke_fishron", new Vector3(0, 6f, 12f));
        duke.Life = (int)(duke.MaxLife * 0.4f);
        Run(g, 5);
        Assert.Equal(1, duke.Stage);
        duke.Life = (int)(duke.MaxLife * 0.1f);
        Run(g, 5);
        Assert.Equal(2, duke.Stage);
    }

    [Fact]
    public void EmpressIsLethalByDay()
    {
        using var g = Arena();
        var empress = Boss(g, "empress_of_light", new Vector3(0, 6f, 12f));
        Run(g, 35);
        Assert.False(empress.Frenzied);
        Assert.True(g.World.Npcs.ContactBase(empress) < 1000);

        g.World.Session.Clock.SetHour(12f);
        Run(g, 35);
        Assert.Contains(empress, g.World.Npcs.All); // she stays for the day
        Assert.True(empress.Frenzied);
        Assert.Equal(9999, g.World.Npcs.ContactBase(empress));
        var seen = new HashSet<Projectile>();
        Run(g, 400, () => g.World.Projectiles.All.Any(p => p.Owner == null && seen.Add(p) && p.Damage >= 9999));
        Assert.Contains(g.World.Projectiles.All, p => p.Owner == null && p.Damage >= 9999);

        g.World.Session.Clock.SetHour(22f); // the rage holds once it starts
        Run(g, 35);
        Assert.True(empress.Frenzied);
    }
}
