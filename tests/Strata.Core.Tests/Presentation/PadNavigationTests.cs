using System.Collections.Generic;
using System.Numerics;
using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class PadNavigationTests
{
    /// <summary>A 10×5 grid of 40 px slots 4 px apart (the backpack), then a 3-slot column to its right.</summary>
    private static List<NavRect> Backpack()
    {
        var rects = new List<NavRect>();
        for (int i = 0; i < 50; i++) rects.Add(new NavRect(i % 10 * 44, i / 10 * 44, 40, 40));
        for (int i = 0; i < 3; i++) rects.Add(new NavRect(460, 10 + i * 30, 26, 26)); // smaller, offset coin slots
        return rects;
    }

    [Fact]
    public void StepsCellByCellThroughAGrid()
    {
        var r = Backpack();
        Assert.Equal(13, FocusNavigation.Next(r, r[12], NavDirection.Right, 12));
        Assert.Equal(11, FocusNavigation.Next(r, r[12], NavDirection.Left, 12));
        Assert.Equal(22, FocusNavigation.Next(r, r[12], NavDirection.Down, 12));
        Assert.Equal(2, FocusNavigation.Next(r, r[12], NavDirection.Up, 12));
    }

    [Fact]
    public void EdgesStopOrJumpToTheNextPanel()
    {
        var r = Backpack();
        Assert.Equal(-1, FocusNavigation.Next(r, r[0], NavDirection.Left, 0));
        Assert.Equal(-1, FocusNavigation.Next(r, r[0], NavDirection.Up, 0));
        Assert.Equal(-1, FocusNavigation.Next(r, r[45], NavDirection.Down, 45));
        // From the end of the first row the cursor jumps to the coin column, to the slot level with it.
        Assert.Equal(50, FocusNavigation.Next(r, r[9], NavDirection.Right, 9));
        // And back into the grid on the row it lines up with.
        int back = FocusNavigation.Next(r, r[51], NavDirection.Left, 51);
        Assert.Equal(9, back % 10);
    }

    [Fact]
    public void PrefersTheSameRowOverACloserDiagonal()
    {
        var rects = new List<NavRect>
        {
            new(0, 0, 40, 40), // from
            new(50, 45, 40, 40), // close, but in the row below
            new(200, 0, 40, 40), // far, in the same row
        };
        Assert.Equal(2, FocusNavigation.Next(rects, rects[0], NavDirection.Right, 0));
    }

    [Fact]
    public void NearestPicksTheClosestCentre()
    {
        var r = Backpack();
        Assert.Equal(0, FocusNavigation.Nearest(r, 0, 0));
        Assert.Equal(23, FocusNavigation.Nearest(r, 3 * 44 + 20, 2 * 44 + 20));
        Assert.Equal(-1, FocusNavigation.Nearest(new List<NavRect>(), 0, 0));
    }

    [Fact]
    public void LookStickHasADeadZoneAndACurve()
    {
        Assert.Equal(Vector2.Zero, PadLook.Shape(new Vector2(0.1f, 0.05f), 0.15f, 0.95f, 2f));
        var full = PadLook.Shape(new Vector2(1f, 0f), 0.15f, 0.95f, 2f);
        Assert.Equal(1f, full.X, 4);
        var half = PadLook.Shape(new Vector2(0f, -0.55f), 0.15f, 0.95f, 2f);
        Assert.Equal(0f, half.X, 4);
        Assert.InRange(-half.Y, 0.2f, 0.3f); // (0.4/0.8)^2 = 0.25: fine aim near the centre
        var diagonal = PadLook.Shape(new Vector2(0.6f, 0.6f), 0.15f, 0.95f, 1f);
        Assert.Equal(diagonal.X, diagonal.Y, 4); // the direction is kept
        var rates = PadLook.Rates(new Vector2(1f, 1f), new PadConfig { LookSpeed = 180, PitchRatio = 0.5f }, 1f);
        Assert.Equal(System.MathF.PI, rates.X, 4);
        Assert.Equal(System.MathF.PI / 2, rates.Y, 4);
    }
}
