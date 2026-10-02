using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The Dungeon's biome-chest weapons: latching piranhas, eater-bursting javelins, life-stealing knives and the
/// frost-hydra sentry and desert-tiger minion.</summary>
public class BiomeWeaponTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.Vitals.MaxMana = 400;
        g.Player.State.Vitals.RestoreMana(400);
        return g;
    }

    private static Npc Dummy(TestGame g, float meters)
    {
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get("green_slime"), g.Player.Position + new Vector3(0, 0.05f, meters))!;
        npc.Life = npc.MaxLife = 5000;
        npc.Timer = -100_000; // sit still
        return npc;
    }

    private static void Fire(TestGame g, string weapon, Npc at)
    {
        g.Hold(weapon);
        g.Player.Aim = Vector3.Normalize(at.Center - g.Player.Eye);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
    }

    [Fact]
    public void PiranhasLatchAndGnaw()
    {
        using var g = Arena();
        var gel = Dummy(g, 5f);
        Fire(g, "piranha_gun", gel);
        var piranha = g.World.Projectiles.All.Single();
        for (int i = 0; i < 40 && piranha.LatchedTo == null; i++) g.Ticks(1);
        Assert.Same(gel, piranha.LatchedTo);
        int bitten = gel.Life;
        g.Ticks(61);
        Assert.InRange(bitten - gel.Life, 6 * 20, 7 * 60); // about six more bites a second
        gel.Position += new Vector3(1.5f, 0, 0);
        g.Ticks(1);
        Assert.True(Vector3.Distance(piranha.Position, gel.Center) < 1.2f, "the piranha let go of a moving target");
        Assert.Contains(piranha, g.World.Projectiles.All);
        g.Ticks(300);
        Assert.DoesNotContain(piranha, g.World.Projectiles.All); // it gives up when its time runs out
    }

    [Fact]
    public void ScourgeReleasesTinyEaters()
    {
        using var g = Arena();
        var gel = Dummy(g, 5f);
        int eaters = 0;
        g.World.Projectiles.Spawned += p => { if (p.Def.Id == "tiny_eater") eaters++; };
        Fire(g, "scourge_of_the_corruptor", gel);
        for (int i = 0; i < 60 && eaters == 0; i++) g.Ticks(1);
        Assert.Equal(3, eaters);
        Assert.True(gel.Life < 5000);
        int afterJavelin = gel.Life;
        g.Ticks(60);
        Assert.True(gel.Life < afterJavelin, "the tiny eaters never bit");
    }

    [Fact]
    public void VampireKnivesHealTheThrower()
    {
        using var g = Arena();
        var gel = Dummy(g, 3f);
        var vitals = g.Player.State.Vitals;
        vitals.Life = 50;
        Fire(g, "vampire_knives", gel);
        Assert.Equal(4, g.World.Projectiles.All.Count(p => p.Def.Id == "vampire_knife"));
        bool healed = false;
        for (int i = 0; i < 40 && !healed; i++)
        {
            int life = vitals.Life, gelLife = gel.Life;
            g.Ticks(1);
            if (gel.Life < gelLife) healed = vitals.Life - life >= 2; // 7.5 % of 29 per knife that cuts
        }
        Assert.True(healed, "a knife cut without healing the thrower");
    }

    [Fact]
    public void FrostHydraSentryFires()
    {
        using var g = Arena();
        int bolts = 0;
        g.World.Projectiles.Spawned += p => { if (p.Def.Id == "frost_hydra_bolt") bolts++; };
        g.Hold("staff_of_the_frost_hydra");
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(40);
        var hydra = g.World.Projectiles.MinionsOf(g.Player).Single();
        var post = hydra.Position;
        var gel = Dummy(g, 8f);
        g.Ticks(150);
        Assert.True(bolts >= 3, $"the hydra fired {bolts} bolts");
        Assert.True(gel.Life < 5000, "no bolt hit");
        Assert.True(Vector3.Distance(post, hydra.Position) < 0.3f, "a sentry stays put");

        // A sentry has its own slot: summoning a minion keeps it.
        g.Player.ResetUse();
        g.Hold("desert_tiger_staff");
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(2, g.World.Projectiles.MinionsOf(g.Player).Count());
    }

    [Fact]
    public void DesertTigerFollowsAndAttacks()
    {
        using var g = Arena();
        g.Hold("desert_tiger_staff");
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(120);
        var tiger = g.World.Projectiles.MinionsOf(g.Player).Single();
        Assert.Equal("desert_tiger", tiger.Def.Id);
        Assert.True(Vector3.Distance(tiger.Position, g.Player.Center) < 4f, "the tiger wandered off");
        var gel = Dummy(g, 6f);
        g.Ticks(240);
        Assert.True(gel.Life < 5000, "the tiger never attacked");
    }
}
