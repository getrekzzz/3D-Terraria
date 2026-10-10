using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

public class ColorVisionTests
{
    private static readonly ColorVisionPalette Palette = ColorVisionPalette.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    [Fact]
    public void OffAndZeroStrengthAreTheIdentity()
    {
        var identity = new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        Assert.Equal(identity, ColorVision.Correction(ColorBlindMode.Off, 1f));
        Assert.Equal(identity, ColorVision.Correction(ColorBlindMode.Deuteranopia, 0f));
        var c = new Rgb(200, 90, 40);
        Assert.Equal(c, ColorVision.Transform(c, identity));
    }

    [Theory]
    [InlineData(ColorBlindMode.Protanopia)]
    [InlineData(ColorBlindMode.Deuteranopia)]
    [InlineData(ColorBlindMode.Tritanopia)]
    public void GreysPassThroughUnchanged(ColorBlindMode mode)
    {
        // The simulation keeps neutral colours neutral, so the correction leaves greys (and black and white) alone.
        var m = ColorVision.Correction(mode, 1f);
        foreach (byte v in new byte[] { 0, 60, 128, 200, 255 })
        {
            var grey = ColorVision.Transform(new Rgb(v, v, v), m);
            Assert.InRange(grey.R, v - 2, v + 2);
            Assert.InRange(grey.G, v - 2, v + 2);
            Assert.InRange(grey.B, v - 2, v + 2);
        }
    }

    [Theory]
    [InlineData(ColorBlindMode.Protanopia)]
    [InlineData(ColorBlindMode.Deuteranopia)]
    public void TheFilterSeparatesRedFromGreenForRedGreenBlindness(ColorBlindMode mode)
    {
        // Grass green against crimson red, and a red heart against a leaf: further apart as the viewer sees them.
        var m = ColorVision.Correction(mode, 1f);
        foreach (var (a, b) in new[] { (new Rgb(90, 160, 60), new Rgb(170, 60, 50)), (new Rgb(220, 46, 56), new Rgb(80, 150, 70)) })
        {
            double before = ColorVision.SeenDistance(a, b, mode);
            double after = ColorVision.SeenDistance(ColorVision.Transform(a, m), ColorVision.Transform(b, m), mode);
            Assert.True(after > before * 1.3, $"{mode}: {a} vs {b} {before:0.0} -> {after:0.0}");
        }
    }

    [Fact]
    public void TheFilterSeparatesBlueFromGreenForTritanopia()
    {
        var m = ColorVision.Correction(ColorBlindMode.Tritanopia, 1f);
        var (a, b) = (new Rgb(70, 140, 200), new Rgb(70, 160, 110)); // water blue against jungle green
        double before = ColorVision.SeenDistance(a, b, ColorBlindMode.Tritanopia);
        double after = ColorVision.SeenDistance(ColorVision.Transform(a, m), ColorVision.Transform(b, m), ColorBlindMode.Tritanopia);
        Assert.True(after > before * 1.2, $"{before:0.0} -> {after:0.0}");
    }

    [Fact]
    public void HalfStrengthSitsBetween()
    {
        var full = ColorVision.Correction(ColorBlindMode.Deuteranopia, 1f);
        var half = ColorVision.Correction(ColorBlindMode.Deuteranopia, 0.5f);
        for (int i = 0; i < 9; i++)
        {
            float identity = i % 4 == 0 ? 1f : 0f;
            Assert.Equal(identity + (full[i] - identity) * 0.5f, half[i], 4);
        }
    }

    [Fact]
    public void ModeNamesRoundTrip()
    {
        foreach (ColorBlindMode mode in Enum.GetValues<ColorBlindMode>())
            Assert.Equal(mode, ColorVision.Parse(ColorVision.Name(mode)));
        Assert.Equal(ColorBlindMode.Off, ColorVision.Parse("purple"));
        Assert.Equal(ColorBlindMode.Off, ColorVision.Parse(null));
        Assert.Equal(ColorBlindMode.Tritanopia, ColorVision.Parse(" Tritanopia "));
    }

    [Fact]
    public void ShippedPaletteKeepsEveryGroupApartForEveryMode()
    {
        Assert.Empty(Palette.Validate());
        foreach (var mode in new[] { ColorBlindMode.Protanopia, ColorBlindMode.Deuteranopia, ColorBlindMode.Tritanopia })
            foreach (var group in Palette.Groups.Keys)
            {
                double before = Palette.ClosestPair(group, mode, remapped: false);
                double after = Palette.ClosestPair(group, mode);
                Assert.True(after >= Palette.MinDistance && after >= before, $"{mode} {group}: {before:0.0} -> {after:0.0}");
            }
        // Terraria's Green and Orange rarities look alike to a deuteranope until remapped.
        Assert.True(Palette.ClosestPair("rarity", ColorBlindMode.Deuteranopia, remapped: false) < Palette.MinDistance);
    }

    [Fact]
    public void PaletteRemapsOnlyListedColoursAndNothingWhenOff()
    {
        var blue = new Rgb(150, 150, 255); // rarity Blue
        var white = new Rgb(255, 255, 255);
        Assert.Equal(blue, Palette.Apply(blue, ColorBlindMode.Off));
        Assert.NotEqual(blue, Palette.Apply(blue, ColorBlindMode.Deuteranopia));
        Assert.Equal(white, Palette.Apply(white, ColorBlindMode.Deuteranopia));
        Assert.Equal(new Rgb(1, 2, 3), Palette.Apply(new Rgb(1, 2, 3), ColorBlindMode.Protanopia));
    }

    [Fact]
    public void ABrokenPaletteIsReported()
    {
        var bad = new ColorVisionPalette
        {
            Groups = { ["pair"] = new() { "#96ff96", "#ffc896" } }, // Green and Orange: confusable for a deuteranope
            Modes = { ["deuteranopia"] = new() { ["#12"] = "#000000" }, ["sepia"] = new() },
        };
        var problems = bad.Validate();
        Assert.Contains(problems, p => p.Contains("sepia"));
        Assert.Contains(problems, p => p.Contains("#12"));
        var close = new ColorVisionPalette { Groups = { ["pair"] = new() { "#96ff96", "#ffc896" } } };
        Assert.Contains(close.Validate(), p => p.Contains("deuteranopia") && p.Contains("pair"));
    }
}
