using System.Collections.Generic;
using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

/// <summary>The boss bar: one bar per boss group, pairs summed, worms counted once, only flagged parts on it.</summary>
public class BossBarModelTests
{
    private static TestGame Arena()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.Player.State.BonusLife = 100_000;
        g.Player.State.Recompute();
        g.Player.State.Vitals.Life = g.Player.State.Vitals.MaxLife;
        return g;
    }

    private static Npc Boss(TestGame g, string id) =>
        g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + new Vector3(0, 6f, 16f))!;

    private static BossBarState Bar(TestGame g) => Assert.IsType<BossBarState>(BossBarModel.Compute(g.World.Npcs.All));

    [Fact]
    public void NoBossNoBar()
    {
        using var g = Arena();
        Boss(g, "green_slime");
        Assert.Null(BossBarModel.Compute(g.World.Npcs.All));
    }

    [Fact]
    public void TwinsCombineIntoOneBar()
    {
        using var g = Arena();
        var ray = Boss(g, "retinazer");
        var flare = Assert.Single(g.World.Npcs.All, n => n.Def.Id == "spazmatism");
        ray.Life -= 1000;
        var bar = Bar(g);
        Assert.Equal("The Twins", bar.Title);
        Assert.Equal("spazmatism", bar.IconId); // the larger leads
        Assert.Equal(ray.MaxLife + flare.MaxLife, bar.Max);
        Assert.Equal(ray.Life + flare.Life, bar.Life);
        g.World.Npcs.Kill(ray);
        bar = Bar(g);
        Assert.Equal(flare.MaxLife, bar.Max); // a fallen twin drops out
        Assert.Equal(1f, bar.Fraction);
    }

    [Fact]
    public void MinorBossesIgnored()
    {
        using var g = Arena();
        Boss(g, "wyvern");
        Assert.Null(BossBarModel.Compute(g.World.Npcs.All));
        var slime = Boss(g, "king_slime");
        var bar = Bar(g);
        Assert.Equal("King Slime", bar.Title);
        Assert.Equal(slime.MaxLife, bar.Max);
    }

    [Fact]
    public void WormCountsOnce()
    {
        using var g = Arena();
        var worm = Boss(g, "eater_of_worlds");
        Assert.Equal(worm.WholeMaxLife, Bar(g).Max);
        Assert.Equal(worm.TotalLife, Bar(g).Life);
        Assert.True(g.World.Npcs.Hit(worm, null, 99_999, DamageClass.None, 0, 0f, worm.Center, melee: false, segment: 20));
        var pieces = g.World.Npcs.All.Where(n => n.Alive && n.Def.Id == "eater_of_worlds").ToList();
        Assert.Equal(2, pieces.Count);
        var bar = Bar(g);
        Assert.Equal(worm.WholeMaxLife, bar.Max); // the whole worm's life, not once per piece
        Assert.Equal(pieces.Sum(p => (long)p.TotalLife), bar.Life);
        Assert.Equal(g.World.Npcs.GroupLife(worm.Def.Boss!), ((int)bar.Life, (int)bar.Max));
    }

    [Fact]
    public void OnlyFlaggedPartsCount()
    {
        using var g = Arena();
        var skeletron = Boss(g, "skeletron");
        Assert.Contains(g.World.Npcs.All, n => n.Leader == skeletron && n.IsPart);
        Assert.Equal(skeletron.MaxLife, Bar(g).Max); // Terraria shows only the head
        g.World.Npcs.Kill(skeletron);
        g.World.Tick();
        Assert.Null(BossBarModel.Compute(g.World.Npcs.All));

        var golem = Boss(g, "golem");
        var head = Assert.Single(g.World.Npcs.All, n => n.Leader == golem && n.Def.Id == "golem_head");
        var bar = Bar(g);
        Assert.Equal("golem", bar.IconId);
        Assert.Equal(golem.MaxLife + head.MaxLife, bar.Max); // the head, not the fists
    }

    [Fact]
    public void ADestroyedPartDropsOut()
    {
        using var g = Arena();
        var core = Boss(g, "moon_lord");
        var parts = g.World.Npcs.All.Where(n => n.Leader == core).ToList();
        Assert.Equal(core.MaxLife + parts.Sum(p => (long)p.MaxLife), Bar(g).Max);
        var hand = parts.First(p => p.Def.Id == "moon_lord_hand");
        g.World.Npcs.Kill(hand);
        var bar = Bar(g);
        Assert.Equal("Moon Lord", bar.Title);
        Assert.Equal("moon_lord", bar.IconId);
        Assert.Equal(core.MaxLife + parts.Where(p => p != hand).Sum(p => (long)p.MaxLife), bar.Max);
    }

    private static List<BossBarState> Bars(TestGame g)
    {
        var bars = new List<BossBarState>();
        BossBarModel.Compute(g.World.Npcs.All, bars);
        return bars;
    }

    [Fact]
    public void EachBossGroupGetsItsOwnBarLargestFirst()
    {
        using var g = Arena();
        var slime = Boss(g, "king_slime");
        var eye = Boss(g, "eye_of_cthulhu");
        var bars = Bars(g);
        Assert.Equal(2, bars.Count);
        var (big, small) = eye.MaxLife >= slime.MaxLife ? (eye, slime) : (slime, eye);
        Assert.Equal(big.Def.Boss!.Title, bars[0].Title);
        Assert.Equal(small.Def.Boss!.Title, bars[1].Title);
        Assert.Equal(small.MaxLife, bars[1].Max);
        Assert.NotEqual(bars[0].Group, bars[1].Group);
        // The single-bar form is the first of them.
        Assert.Equal(bars[0], Bar(g));
    }

    [Fact]
    public void TheTwinsShareOneBarBesideAnotherMechanicalBoss()
    {
        using var g = Arena();
        Boss(g, "retinazer");
        var prime = Boss(g, "skeletron_prime");
        var bars = Bars(g);
        Assert.Equal(2, bars.Count);
        Assert.Contains(bars, b => b.Title == "The Twins");
        var primeBar = Assert.Single(bars, b => b.Title == prime.Def.Boss!.Title);
        Assert.Equal("skeletron_prime", primeBar.IconId);
        Assert.Equal(BossBarModel.Compute(g.World.Npcs.All)!.Value.Group, bars[0].Group);
    }

    [Fact]
    public void UnflaggedMinibossesEachGetABarAndAtMostThreeShow()
    {
        using var g = Arena();
        Boss(g, "mourning_wood");
        Boss(g, "mourning_wood");
        Assert.Equal(2, Bars(g).Count);
        Boss(g, "pumpking");
        Boss(g, "king_slime");
        var bars = Bars(g);
        Assert.Equal(BossBarModel.MaxBars, bars.Count);
        Assert.True(bars[0].Max >= bars[1].Max && bars[1].Max >= bars[2].Max);
        Assert.DoesNotContain(bars, b => b.Title == "King Slime"); // the smallest group is left off
    }
}
