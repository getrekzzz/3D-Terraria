using Strata.Core.Common;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

public class HazardTests
{
    private static TestGame SpikeUnderfoot()
    {
        var g = TestGame.Create();
        var spike = new TileObjectInstance(g.Content.TileObjects.Get("spike"), g.Floor + Int3.Up, 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(spike));
        g.Player.State.Vitals.ImmunityTicks = 0;
        return g;
    }

    [Fact]
    public void SpikesHurtWithImmunityFrames()
    {
        using var g = SpikeUnderfoot();
        var vitals = g.Player.State.Vitals;
        int full = vitals.Life;
        int hurts = 0;
        g.World.Npcs.PlayerHurt += (_, _, _) => hurts++;
        g.Ticks(1);
        int afterFirst = vitals.Life;
        Assert.InRange(full - afterFirst, 30, 50); // 40 damage, no armour
        Assert.Equal(1, hurts);
        g.Ticks(30); // still inside the immunity window
        Assert.Equal(1, hurts);
        Assert.True(vitals.Life <= afterFirst + 1);
        g.Ticks(20);
        Assert.Equal(2, hurts);
        Assert.True(vitals.Life < afterFirst);
    }

    [Fact]
    public void SpikesRespectGodMode()
    {
        using var g = SpikeUnderfoot();
        var vitals = g.Player.State.Vitals;
        vitals.Undying = true;
        g.Ticks(400);
        Assert.False(vitals.IsDead);
        Assert.InRange(vitals.Life, 1, 10);
    }

    [Fact]
    public void SpikesLeaveAPlayerStandingBesideThemAlone()
    {
        using var g = TestGame.Create();
        var spike = new TileObjectInstance(g.Content.TileObjects.Get("spike"), g.Floor + new Int3(3, 1, 0), 0, 0);
        Assert.True(g.World.Session.World.TileObjects.TryAdd(spike));
        g.Player.State.Vitals.ImmunityTicks = 0;
        int full = g.Player.State.Vitals.Life;
        g.Ticks(60);
        Assert.Equal(full, g.Player.State.Vitals.Life);
    }
}
