using System.Numerics;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>
/// God mode (<see cref="Vitals.Undying"/>, the <c>god</c> command smoke scripts lean on): every way the world can hurt
/// the player stops at 1 life (contact swarms, shots, lava, debuffs, falls, drowning), and nothing is counted as a death.
/// Turning it off at 1 life with a debuff still ticking kills on the next tick, which is why scripts heal and clear
/// debuffs before <c>god off</c>.
/// </summary>
public class GodModeTests
{
    private static TestGame Godly(List<string> slain)
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Liquids.Enabled = false;
        g.World.PlayerSlain += (_, text) => slain.Add(text);
        g.Player.State.Vitals.Undying = true;
        return g;
    }

    private static void AssertSurvived(TestGame g, List<string> slain)
    {
        Assert.Empty(slain);
        Assert.True(g.Player.Alive);
        Assert.Equal(0, g.Player.State.Deaths);
        Assert.InRange(g.Player.State.Vitals.Life, 1, g.Player.State.Vitals.MaxLife);
    }

    [Fact]
    public void AContactSwarmAndTheirShotsNeverKill()
    {
        var slain = new List<string>();
        using var g = Godly(slain);
        g.Player.State.Vitals.Life = 3;
        int hits = 0;
        g.World.Npcs.PlayerHurt += (_, _, _) => hits++;
        foreach (var (id, dx) in new[] { ("zombie", 0.6f), ("zombie", -0.6f), ("skeleton", 0.4f), ("hornet", -0.4f), ("fire_imp", 0.8f) })
            g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + new Vector3(dx, 0, 0.3f));
        var archer = g.Content.Enemies.Get("demon");
        for (int i = 0; i < 6; i++)
        {
            var shot = g.World.Projectiles.Spawn(g.Content.Projectiles.Get(archer.Ranged!.Projectile), g.Player.Center + new Vector3(0, 0, 1.5f + i),
                new Vector3(0, 0, -20f), null, 500, 2f, Strata.Core.Content.Defs.DamageClass.None, 0);
            shot.Shooter = archer;
        }
        g.Ticks(600);
        Assert.True(hits > 3); // the swarm and the shots really did land
        AssertSurvived(g, slain);
    }

    [Fact]
    public void LavaAndEveryDebuffTickStopAtOneLife()
    {
        var slain = new List<string>();
        using var g = Godly(slain);
        var feet = g.Player.FeetCell;
        g.World.Editor.FillLiquid(feet, feet + Common.Int3.Up, g.Content.Liquids.Get("lava"), LiquidCode.MaxLevel);
        foreach (var id in new[] { "poisoned", "on_fire", "bleeding", "venom", "cursed_inferno" })
            if (g.Content.Buffs.TryGet(id, out var debuff)) g.Player.State.Buffs.Add(debuff, 3600);
        g.Player.State.Recompute();
        Assert.True(g.Player.State.Effects.DamagePerSecond > 0);
        g.Ticks(900);
        AssertSurvived(g, slain);
        Assert.Equal(1, g.Player.State.Vitals.Life);
    }

    [Fact]
    public void FallsDrowningAndDirectDamageStopAtOneLife()
    {
        var slain = new List<string>();
        using var g = Godly(slain);
        g.World.HurtPlayerDirect(g.Player, 100_000, harm: Harm.Fall);
        g.World.HurtPlayerDirect(g.Player, int.MaxValue, harm: Harm.Other);
        Assert.Equal(1, g.Player.State.Vitals.Life);
        g.Player.State.Vitals.ImmunityTicks = 0;
        Assert.True(g.Player.State.Vitals.Hurt(int.MaxValue, 0)); // the hit lands (knockback, effects), the life stays
        Assert.Equal(1, g.Player.State.Vitals.Life);
        var feet = g.Player.FeetCell;
        g.World.Editor.FillLiquid(feet, feet + new Common.Int3(0, 5, 0), g.Content.Liquids.Get("water"), LiquidCode.MaxLevel);
        g.Ticks(PlayerEntity.MaxBreath + 600); // out of breath for ten seconds
        Assert.Equal(0, g.Player.Breath);
        AssertSurvived(g, slain);
    }

    [Fact]
    public void TurningGodModeOffWithADebuffTickingCanKill()
    {
        var slain = new List<string>();
        using var g = Godly(slain);
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("on_fire"), 3600);
        g.Player.State.Recompute();
        g.World.HurtPlayerDirect(g.Player, 10_000, harm: Harm.Other); // worn down to the floor, as in a long fight
        g.Ticks(180);
        AssertSurvived(g, slain);
        Assert.Equal(1, g.Player.State.Vitals.Life); // burning stops natural regeneration
        g.Player.State.Vitals.Undying = false;
        g.Ticks(120);
        Assert.Single(slain);
        Assert.Equal(1, g.Player.State.Deaths);
    }
}
