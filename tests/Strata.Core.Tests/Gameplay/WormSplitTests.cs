using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>The Eater of Worlds: per-segment life, splitting into pieces, head hand-off, segment drops, one defeat, Expert spit.</summary>
public class WormSplitTests
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

    private static Npc Eater(TestGame g) => g.World.Npcs.Spawn(g.Content.Enemies.Get("eater_of_worlds"), g.Player.Position + new Vector3(0, 5f, 6f))!;

    private static List<Npc> Pieces(TestGame g) => g.World.Npcs.All.Where(n => n.Alive && n.Def.Id == "eater_of_worlds").ToList();

    private static void CutSegment(TestGame g, Npc worm, int segment) =>
        Assert.True(g.World.Npcs.Hit(worm, null, 99_999, DamageClass.None, 0, 0f, worm.Center, melee: false, segment: segment));

    [Fact]
    public void SegmentsHaveTerrariaLife()
    {
        using var g = Arena();
        var worm = Eater(g);
        Assert.Equal(65, worm.Life);
        Assert.Equal(49, worm.SegmentLife.Count);
        Assert.Equal(150, worm.SegmentLife[0]);
        Assert.Equal(220, worm.SegmentLife[^1]);
        Assert.Equal(65 + 48 * 150 + 220, worm.TotalLife);
        Assert.Equal(worm.TotalLife, worm.WholeMaxLife);
    }

    [Fact]
    public void SplittingAtTheCreatureCapKeepsTheBackPiece()
    {
        using var g = Arena();
        var worm = Eater(g);
        var slime = g.Content.Enemies.Get("green_slime");
        while (g.World.Npcs.Spawn(slime, g.Player.Position + new Vector3(30f, 1f, 0)) != null) { }
        Assert.Equal(NpcSystem.MaxNpcs, g.World.Npcs.All.Count);
        int before = worm.TotalLife;
        CutSegment(g, worm, 20);
        Assert.Equal(2, Pieces(g).Count); // the segments were already in the world: the cut does not lose them
        Assert.Equal(before - 150, Pieces(g).Sum(p => p.TotalLife));
    }

    [Fact]
    public void KillingAMiddleSegmentSplitsTheWorm()
    {
        using var g = Arena();
        var worm = Eater(g);
        var spawned = new List<Npc>();
        g.World.Npcs.BossSpawned += spawned.Add;
        var cutAt = worm.Segments[20];
        var back = worm.Segments[21];
        CutSegment(g, worm, 20);
        var pieces = Pieces(g);
        Assert.Equal(2, pieces.Count);
        Assert.Empty(spawned); // no second "has awoken"
        Assert.Equal(20, worm.Segments.Count);
        Assert.Equal(20, worm.SegmentLife.Count);
        var other = pieces.Single(p => p != worm);
        Assert.Equal(back, other.Center);
        Assert.Equal(150, other.Life); // a body segment become a head
        Assert.Equal(49 - 21 - 1, other.Segments.Count);
        Assert.Equal(other.Segments.Count, other.SegmentLife.Count);
        Assert.Equal(1 + 20 + 1 + other.Segments.Count, 50 - 1); // the cut segment is gone
        Assert.Equal(220, other.SegmentLife[^1]);
        Assert.Equal(worm.WholeMaxLife, other.WholeMaxLife);
        Assert.Equal((worm.TotalLife + other.TotalLife, worm.WholeMaxLife), g.World.Npcs.GroupLife(worm.Def.Boss!));
        Assert.True(Vector3.Distance(cutAt, back) < 1.5f);
        g.Ticks(30); // both pieces keep hunting on their own
        Assert.Equal(2, Pieces(g).Count);
        Assert.Equal(20, worm.Segments.Count);
    }

    [Fact]
    public void KillingTheHeadPromotesTheNextSegment()
    {
        using var g = Arena();
        var worm = Eater(g);
        var next = worm.Segments[0];
        Assert.True(g.World.Npcs.Hit(worm, null, 99_999, DamageClass.None, 0, 0f, worm.Center, melee: false));
        Assert.True(worm.Alive);
        Assert.False(worm.Removed);
        Assert.Equal(48, worm.Segments.Count);
        Assert.Equal(150, worm.Life);
        Assert.Equal(next, worm.Center);
        Assert.Single(Pieces(g));
    }

    [Fact]
    public void CuttingNextToTheTailOrHeadLeavesNoLoneSegment()
    {
        using var g = Arena();
        var worm = Eater(g);
        CutSegment(g, worm, 47); // only the tail is behind: it dies with the cut
        Assert.Single(Pieces(g));
        Assert.Equal(47, worm.Segments.Count);
        CutSegment(g, worm, 0); // the head is left alone: it dies, and the rest crawls on
        var rest = Assert.Single(Pieces(g));
        Assert.NotEqual(worm, rest);
        Assert.Equal(45, rest.Segments.Count);
        Assert.False(g.World.Progression.IsSet("defeated_eater_of_worlds"));
        CutSegment(g, rest, 1); // a head and one segment stay; the rest crawls off
        Assert.Single(rest.Segments);
        g.World.Npcs.Kill(Pieces(g).Single(p => p != rest));
        CutSegment(g, rest, 0); // the last segment goes, then the lone head: the boss falls
        Assert.True(rest.Removed);
        Assert.Empty(Pieces(g));
        Assert.True(g.World.Progression.IsSet("defeated_eater_of_worlds"));
    }

    [Fact]
    public void TheBossFallsWithItsLastPieceAndDropsLootOnce()
    {
        using var g = Arena();
        var worm = Eater(g);
        int defeats = 0;
        g.World.Npcs.BossDefeated += _ => defeats++;
        CutSegment(g, worm, 30);
        CutSegment(g, worm, 10);
        Assert.Equal(3, Pieces(g).Count);
        int scalesBefore = 0;
        foreach (var piece in Pieces(g).OrderBy(p => p.Id).ToList())
        {
            Assert.False(g.World.Progression.IsSet("defeated_eater_of_worlds"));
            scalesBefore = g.World.Drops.Items.Count;
            g.World.Npcs.Kill(piece);
        }
        Assert.Equal(1, defeats);
        Assert.True(g.World.Progression.IsSet("defeated_eater_of_worlds"));
        Assert.True(g.World.Progression.IsSet("defeated_evil_boss"));
        // The boss table (40+ ore) rolled exactly once: only one big ore stack.
        Assert.Equal(1, g.World.Drops.Items.Count(d => d.Stack.Item!.Id == "demonite_ore" && d.Stack.Count >= 30));
    }

    [Fact]
    public void KilledSegmentsDropScalesAndOre()
    {
        using var g = Arena();
        var worm = Eater(g);
        for (int i = 0; i < 12; i++) CutSegment(g, worm, worm.Segments.Count - 3); // nibble the tail end off
        var drops = g.World.Drops.Items.Where(d => d.Stack.Count <= 2).Select(d => d.Stack.Item!.Id).ToList();
        Assert.Contains("shadow_scale", drops);
        Assert.Contains("demonite_ore", drops);
        Assert.False(g.World.Progression.IsSet("defeated_eater_of_worlds"));
    }

    [Theory]
    [InlineData(Difficulty.Classic, false)]
    [InlineData(Difficulty.Expert, true)]
    public void ExpertBodySegmentsSpit(Difficulty difficulty, bool spits)
    {
        using var g = Arena(difficulty);
        var worm = Eater(g);
        // Lay the body out across the arena's air so some segments see the player (spawned, it hangs into the ground).
        for (int i = 0; i < worm.Segments.Count; i++) worm.Segments[i] = worm.Center + new Vector3((i + 1) * 0.5f, -2f, 0);
        int seen = 0;
        for (int t = 0; t < 600; t++)
        {
            g.World.Tick();
            seen = Math.Max(seen, g.World.Projectiles.All.Count(p => p.Def.Id == "eater_spit"));
        }
        Assert.Equal(spits, seen > 0);
    }
}
