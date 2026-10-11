using Strata.Core.Common;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Player;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Movement feel from data/config/movement.json: jump apex, the speed-boot run-up and Sprint.</summary>
public class MovementFeelTests
{
    private static readonly MovementConfig Config = MovementConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void FullJumpPeaksFiveToSixCells()
    {
        float cells = Config.JumpApex() / Units.CellSize;
        Assert.InRange(cells, 5f, 6f);
        Assert.True(Config.JumpApex(1.33f) > Config.JumpApex()); // jump-height bonuses still raise it
    }

    [Fact]
    public void SprintDoesNothingWithoutSpeedBoots()
    {
        Assert.Equal(Config.WalkSpeed, Config.TopSpeed(0, 0, 0, sprint: true), 4);
        Assert.Equal(Config.WalkSpeed * 1.1f, Config.TopSpeed(0.1f, 0, 5f, sprint: true), 4);
    }

    [Fact]
    public void SpeedBootsRampUpOverTheRunUp()
    {
        float walk = Config.WalkSpeed;
        Assert.Equal(walk, Config.TopSpeed(0, 0.45f, 0, false), 4); // no instant boost
        float half = Config.TopSpeed(0, 0.45f, Config.RunUpTime * 0.5f, false);
        Assert.InRange(half, walk * 1.2f, walk * 1.25f); // smoothstep midpoint
        Assert.Equal(walk * 1.45f, Config.TopSpeed(0, 0.45f, Config.RunUpTime, false), 3);
        Assert.Equal(walk * 1.45f, Config.TopSpeed(0, 0.45f, Config.RunUpTime * 3, false), 3);
        Assert.True(Config.TopSpeed(0, 0.45f, Config.RunUpTime, true) > walk * 1.45f + 0.1f); // Sprint boosts boots
    }

    [Fact]
    public void RunUpBuildsOnTheGroundHoldsInTheAirAndResetsOnStopping()
    {
        float walk = Config.WalkSpeed, r = 0;
        for (int i = 0; i < 30; i++) r = Config.AdvanceRunUp(r, 1f / 60, true, true, walk, walk);
        Assert.Equal(0.5f, r, 3);
        Assert.Equal(0.5f, Config.AdvanceRunUp(r, 1f / 60, false, true, walk, walk), 3);
        Assert.Equal(0.5f, Config.AdvanceRunUp(r, 1f / 60, true, true, walk * 0.7f, walk), 3); // slowed: no build
        Assert.Equal(0f, Config.AdvanceRunUp(r, 1f / 60, true, false, walk, walk));
        Assert.Equal(0f, Config.AdvanceRunUp(r, 1f / 60, true, true, walk * 0.3f, walk)); // ran into a wall
        for (int i = 0; i < 600; i++) r = Config.AdvanceRunUp(r, 1f / 60, true, true, walk, walk);
        Assert.Equal(Config.RunUpTime, r, 3);
    }
}
