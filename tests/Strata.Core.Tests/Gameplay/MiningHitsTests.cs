using Strata.Core.Common;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Terraria's mining pace: every tile has 100 health and pickaxes deal power × the tile's scale per hit.</summary>
public class MiningHitsTests
{
    private static int HitsToBreak(string block, int pickPower)
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var cell = g.Floor + new Int3(4, 1, 4);
        var batch = world.BeginEdit();
        batch.SetBlock(cell, g.Content.BlockId(block));
        g.World.Session.Commit(batch);
        for (int hit = 1; hit <= 20; hit++)
        {
            var outcome = g.World.Editor.Mine(new[] { cell }, pickPower, pickPower, hit);
            if (outcome.TooHard.Count > 0) return -1;
            if (outcome.Broken.Count > 0) return hit;
        }
        return int.MaxValue;
    }

    [Theory]
    [InlineData("stone", 35, 3)]
    [InlineData("dirt", 35, 2)]
    [InlineData("sand", 35, 2)]
    [InlineData("stone", 55, 2)]
    [InlineData("cobalt_ore", 100, 2)]
    [InlineData("adamantite_ore", 150, 3)]
    [InlineData("chlorophyte_ore", 200, 3)]
    public void PickaxesBreakTilesInTerrariasHitCounts(string block, int pickPower, int hits) =>
        Assert.Equal(hits, HitsToBreak(block, pickPower));

    [Fact]
    public void HellstoneTinksBelowItsPower() => Assert.Equal(-1, HitsToBreak("hellstone", 55));

    [Fact]
    public void TrackerVersionMovesWithEveryChange()
    {
        var t = new Strata.Core.Gameplay.Mining.BlockDamageTracker { ForgetAfterTicks = 10 };
        int v0 = t.Version;
        t.Hit(Int3.Zero, 10, 0);
        Assert.True(t.Version > v0);
        int v1 = t.Version;
        t.Prune(100);
        Assert.True(t.Version > v1);
        int v2 = t.Version;
        t.Clear(Int3.Zero);
        Assert.Equal(v2, t.Version);
    }
}
