using Strata.Core.Content.Index;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

public class DisplayHelperTests
{
    private static readonly Strata.Core.Content.ContentDatabase Db = TestPaths.Content;

    [Fact]
    public void HeartsFollowTerrariaRules()
    {
        var start = VitalsDisplay.Hearts(100, 100, 100);
        Assert.Equal(5, start.Count);
        Assert.Equal(20f, start.PerIcon);
        Assert.Equal(0, start.Golden);

        Assert.Equal(20, VitalsDisplay.Hearts(400, 400, 400).Count);

        var fruit = VitalsDisplay.Hearts(500, 500, 500);
        Assert.Equal(20, fruit.Count);
        Assert.Equal(25f, fruit.PerIcon);
        Assert.Equal(20, fruit.Golden);
        Assert.Equal(5, VitalsDisplay.Hearts(425, 425, 425).Golden);
        Assert.Equal(0, VitalsDisplay.Hearts(480, 480, 400).Golden); // Lifeforce raises the maximum, not the hearts' gold

        var hurt = VitalsDisplay.Hearts(250, 400, 400);
        Assert.Equal(12, hurt.Full);
        Assert.Equal(1f, hurt.Fill(11));
        Assert.Equal(0.5f, hurt.Fill(12));
        Assert.Equal(0f, hurt.Fill(13));
    }

    [Fact]
    public void StarsAndBubbles()
    {
        var mana = VitalsDisplay.Stars(200, 200);
        Assert.Equal(10, mana.Count);
        Assert.Equal(20f, mana.PerIcon);
        var big = VitalsDisplay.Stars(400, 400);
        Assert.Equal(10, big.Count);
        Assert.Equal(40f, big.PerIcon);
        Assert.Equal(1, VitalsDisplay.Stars(20, 20).Count);

        var air = VitalsDisplay.Bubbles(600, 1200);
        Assert.Equal(10, air.Count);
        Assert.Equal(5, air.Full);
    }

    [Fact]
    public void BuffTimerAndDurationText()
    {
        Assert.Equal("59 s", BuffDisplay.Timer(59 * 60));
        Assert.Equal("1 s", BuffDisplay.Timer(1));
        Assert.Equal("1 m", BuffDisplay.Timer(61 * 60));
        Assert.Equal("5 m", BuffDisplay.Timer(5 * 3600));
        Assert.Equal("1 h", BuffDisplay.Timer(3600 * 60));
        Assert.Equal("30 second duration", BuffDisplay.Duration(30 * 60));
        Assert.Equal("5 minute duration", BuffDisplay.Duration(18000));
        Assert.Equal("1 hour duration", BuffDisplay.Duration(216000));
    }

    [Fact]
    public void CancelAndTimerRules()
    {
        Assert.False(BuffDisplay.CanCancel(Db.Buffs.Get("potion_sickness")));
        Assert.True(BuffDisplay.CanCancel(Db.Buffs.Get("ironskin")));
        Assert.True(BuffDisplay.ShowsTimer(Db.Buffs.Get("ironskin")));
        Assert.False(BuffDisplay.ShowsTimer(Db.Buffs.Get("cozy_fire")));
        Assert.False(BuffDisplay.ShowsTimer(Db.Buffs.Get("heart_lamp")));
        Assert.False(BuffDisplay.ShowsTimer(Db.Buffs.Get("honey")));
    }

    [Fact]
    public void StationNamesReadLikeTerraria()
    {
        Assert.Equal("Iron Anvil or Lead Anvil", StationNames.Describe("anvil", Db));
        Assert.Equal("Work Bench", StationNames.Describe("work_bench", Db));
        Assert.Equal("Mythril Anvil or Orichalcum Anvil", StationNames.Describe("hardmode_anvil", Db));
        Assert.Equal("Placed Bottle or Alchemy Table", StationNames.Describe("alchemy", Db));
        Assert.Equal("Iron Anvil or Lead Anvil, Work Bench", StationNames.Describe(new[] { "anvil", "work_bench" }, Db));
        Assert.Equal("Moon forge", StationNames.Describe("moon_forge", Db)); // nothing provides it: the id as words
    }
}
