using System;
using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class PhotoLensTests
{
    [Fact]
    public void FocusSitsInsideTheSharpZone()
    {
        var d = PhotoLens.Dof(8f, 0.5f);
        Assert.True(d.Enabled);
        Assert.True(d.NearDistance < 8f && d.FarDistance > 8f);
        Assert.True(d.NearTransition <= d.NearDistance);
        Assert.Equal(0.5f * PhotoLens.MaxBlurAmount, d.Amount, 5);
    }

    [Fact]
    public void ZeroBlurDisablesAndInputsClamp()
    {
        Assert.False(PhotoLens.Dof(8f, 0f).Enabled);
        Assert.Equal(PhotoLens.Dof(PhotoLens.MaxFocus, 1f), PhotoLens.Dof(9999f, 5f));
        Assert.True(PhotoLens.Dof(-3f, 1f).NearDistance > 0f);
    }

    [Fact]
    public void ExposureIsInStops()
    {
        Assert.Equal(1f, PhotoLens.ExposureMultiplier(0f), 5);
        Assert.Equal(2f, PhotoLens.ExposureMultiplier(1f), 5);
        Assert.Equal(0.25f, PhotoLens.ExposureMultiplier(-2f), 5);
        Assert.Equal(PhotoLens.ExposureMultiplier(PhotoLens.MaxStops), PhotoLens.ExposureMultiplier(10f), 5);
    }

    [Fact]
    public void FiltersAndNames()
    {
        Assert.Equal(0, PhotoLens.FilterIndex("none"));
        Assert.Equal(4, PhotoLens.FilterIndex("Sepia"));
        Assert.Equal(-1, PhotoLens.FilterIndex("vaporwave"));
        Assert.Equal("20261002-134501-007.png", PhotoLens.ScreenshotName(new DateTime(2026, 10, 2, 13, 45, 1, 7)));
    }
}
