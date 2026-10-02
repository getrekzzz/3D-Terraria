using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class RarityColorsTests
{
    [Theory]
    [InlineData(-1, 130, 130, 130)]
    [InlineData(0, 255, 255, 255)]
    [InlineData(1, 150, 150, 255)]
    [InlineData(2, 150, 255, 150)]
    [InlineData(3, 255, 200, 150)]
    [InlineData(4, 255, 150, 150)]
    [InlineData(5, 255, 150, 255)]
    [InlineData(6, 210, 160, 255)]
    [InlineData(7, 150, 255, 10)]
    [InlineData(8, 255, 255, 10)]
    [InlineData(9, 5, 200, 255)]
    [InlineData(10, 255, 40, 100)]
    [InlineData(11, 180, 40, 255)]
    [InlineData(-11, 255, 175, 0)]
    public void FixedTiersUseTerrariasColours(int rarity, int r, int g, int b)
    {
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), RarityColors.Of(rarity));
        Assert.Equal(RarityColors.Of(rarity), RarityColors.Of(rarity, 12.345)); // fixed tiers ignore time
        Assert.False(RarityColors.IsAnimated(rarity));
    }

    [Fact]
    public void TiersBeyondTheTableClampInsteadOfFallingThroughToYellow()
    {
        Assert.Equal(RarityColors.Of(RarityColors.Purple), RarityColors.Of(12));
        Assert.Equal(RarityColors.Of(RarityColors.Purple), RarityColors.Of(99));
        Assert.Equal(RarityColors.Of(RarityColors.Gray), RarityColors.Of(-5));
        Assert.NotEqual(RarityColors.Of(8), RarityColors.Of(9));
        Assert.NotEqual(RarityColors.Of(8), RarityColors.Of(10));
    }

    [Fact]
    public void ExpertCyclesThroughEveryHueAndRepeats()
    {
        Assert.True(RarityColors.IsAnimated(RarityColors.Expert));
        double cycle = RarityColors.ExpertCycleSeconds;
        Assert.InRange(cycle, 3.0, 4.5);
        Assert.Equal(new Rgb(255, 0, 0), RarityColors.Of(RarityColors.Expert, 0));
        Assert.Equal(new Rgb(0, 255, 0), RarityColors.Of(RarityColors.Expert, cycle / 3));
        Assert.Equal(new Rgb(0, 0, 255), RarityColors.Of(RarityColors.Expert, cycle * 2 / 3));
        Assert.Equal(RarityColors.Of(RarityColors.Expert, 0.7), RarityColors.Of(RarityColors.Expert, 0.7 + cycle * 3));
        for (double t = 0; t < cycle; t += cycle / 97)
        {
            var c = RarityColors.Of(RarityColors.Expert, t);
            Assert.Equal(255, Math.Max(c.R, Math.Max(c.G, c.B))); // always fully saturated and bright
            Assert.Equal(0, Math.Min(c.R, Math.Min(c.G, c.B)));
        }
    }

    [Fact]
    public void MasterPulsesBetweenRedAndOrange()
    {
        Assert.True(RarityColors.IsAnimated(RarityColors.Master));
        double pulse = RarityColors.MasterPulseSeconds;
        Assert.Equal(new Rgb(255, 0, 0), RarityColors.Of(RarityColors.Master, 0));
        Assert.Equal(new Rgb(255, 200, 0), RarityColors.Of(RarityColors.Master, pulse / 2));
        Assert.Equal(new Rgb(255, 100, 0), RarityColors.Of(RarityColors.Master, pulse / 4));
        Assert.Equal(RarityColors.Of(RarityColors.Master, pulse / 4), RarityColors.Of(RarityColors.Master, pulse * 3 / 4));
        for (double t = -3; t < 3; t += 0.037)
        {
            var c = RarityColors.Of(RarityColors.Master, t);
            Assert.Equal(255, c.R);
            Assert.Equal(0, c.B);
            Assert.InRange(c.G, (byte)0, (byte)200);
        }
    }
}
