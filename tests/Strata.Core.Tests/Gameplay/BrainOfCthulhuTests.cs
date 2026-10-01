using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The Brain of Cthulhu: Creeper shield, exposed phase with illusions, and Confused reversing movement.</summary>
public class BrainOfCthulhuTests
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

    private static Npc Brain(TestGame g) => g.World.Npcs.Spawn(g.Content.Enemies.Get("brain_of_cthulhu"), g.Player.Position + new Vector3(0, 3, 6))!;

    private static void KillCreepers(TestGame g)
    {
        foreach (var c in g.World.Npcs.All.Where(n => n.Def.Id == "creeper" && n.Alive).ToList()) g.World.Npcs.Kill(c);
    }

    [Fact]
    public void ShieldedUntilCreepersFallThenCastsThreeIllusions()
    {
        using var g = Arena();
        var brain = Brain(g);
        Assert.Equal(1000, brain.MaxLife);
        g.Ticks(3);
        var creepers = g.World.Npcs.All.Where(n => n.Def.Id == "creeper").ToList();
        Assert.Equal(20, creepers.Count);
        Assert.All(creepers, c => Assert.Equal(100, c.MaxLife));
        Assert.True(brain.Invulnerable);
        Assert.Equal(0, brain.Illusions);
        KillCreepers(g);
        g.Ticks(3);
        Assert.Equal(1, brain.Stage); // exposed at full life, as in Terraria
        Assert.Equal("brain_of_cthulhu_exposed", brain.ModelOverride);
        Assert.False(brain.Invulnerable);
        Assert.Equal(3, brain.Illusions);
        Assert.Equal(g.Player.Center, brain.MirrorAround);
        float d = Vector3.Distance(brain.Center, brain.MirrorAround);
        for (int i = 0; i < 3; i++) Assert.Equal(d, Vector3.Distance(Illusions.Position(brain, i), brain.MirrorAround), 3);
    }

    [Fact]
    public void ConfusionReversesControls()
    {
        using var g = Arena();
        var state = g.Player.State;
        Assert.Equal((1f, -1f), state.Effects.SteerInput(1f, -1f));
        state.Buffs.Add(g.Content.Buffs.Get("confused"), 120);
        state.Recompute();
        Assert.True(state.Effects.InvertControls);
        Assert.Equal((-1f, 1f), state.Effects.SteerInput(1f, -1f));
        Assert.Equal(0f, state.Effects.Stats.Bonus(Strata.Core.Gameplay.Stats.StatIds.MoveSpeed));
        // Confusion never outlives the player: death clears it at once, and the respawned player steers normally.
        state.Vitals.Life = 0;
        g.World.Tick();
        Assert.False(state.Effects.InvertControls);
        g.World.Respawn(g.Player);
        Assert.False(state.Buffs.Has("confused"));
        Assert.False(state.Effects.InvertControls);
    }

    [Theory]
    [InlineData(Difficulty.Classic, false)]
    [InlineData(Difficulty.Expert, true)]
    public void ExpertBrainPulsesConfusionInRange(Difficulty difficulty, bool pulses)
    {
        using var g = Arena(difficulty);
        var brain = Brain(g);
        g.Ticks(2);
        KillCreepers(g);
        int longest = 0;
        for (int t = 0; t < 420; t++)
        {
            g.World.Tick();
            g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
            foreach (var b in g.Player.State.Buffs.Active)
                if (b.Def.Id == "confused") longest = Math.Max(longest, b.TicksLeft);
        }
        Assert.True(brain.Alive);
        // The aura's confusion lasts 120 ticks; a touch's only 90.
        Assert.Equal(pulses, longest > 100);
    }
}
