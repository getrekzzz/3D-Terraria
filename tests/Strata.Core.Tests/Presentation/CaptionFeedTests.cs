using System.Numerics;
using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Presentation;

public class CaptionFeedTests
{
    private static readonly CaptionsConfig Shipped = CaptionsConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));

    private static CaptionsConfig Config(int maxLines = 3) => new()
    {
        Seconds = 3f, FadeSeconds = 0.5f, MaxLines = maxLines, Range = 20f,
        Colors = { ["world"] = "#ffffff", ["boss"] = "#af4bff", ["enemy"] = "#ffaa78", ["town"] = "#327dff", ["weather"] = "#96d2ff" },
        Sounds =
        {
            ["boss_roar"] = new CaptionDef { Text = "Boss roars", Kind = "boss", Range = 80f },
            ["hit_squish"] = new CaptionDef { Text = "Gel squelches", Kind = "enemy" },
            ["thunder_near"] = new CaptionDef { Text = "Thunder cracks", Kind = "weather" },
            ["npc_arrive"] = new CaptionDef { Text = "Resident arrives", Kind = "town" },
        },
    };

    [Fact]
    public void ShippedCaptionsCoverTheImportantSounds()
    {
        Assert.Empty(Shipped.Validate());
        foreach (var id in new[] { "boss_roar", "npc_arrive", "thunder_near", "thunder_far", "rain_light", "rain_heavy", "hit" })
            Assert.True(Shipped.Sounds.ContainsKey(id), id);
        Assert.Contains(Shipped.Sounds.Values, d => d.Kind == "enemy");
    }

    [Fact]
    public void OnlyListedSoundsAreCaptioned()
    {
        var feed = new CaptionFeed(Config());
        Assert.False(feed.Hear("ui_click", 0));
        Assert.False(feed.Hear("step_grass", 0, Vector3.Zero, Vector3.Zero));
        Assert.True(feed.Hear("npc_arrive", 0));
        Assert.Equal(1, feed.Count);
        Assert.Equal("Resident arrives", feed[0].Text);
        Assert.False(feed[0].Positioned);
    }

    [Fact]
    public void FarSoundsAreNotCaptionedUnlessTheirRangeReaches()
    {
        var feed = new CaptionFeed(Config());
        var listener = Vector3.Zero;
        Assert.False(feed.Hear("hit_squish", 0, new Vector3(30, 0, 0), listener)); // beyond the 20 m default
        Assert.True(feed.Hear("hit_squish", 0, new Vector3(10, 0, 0), listener));
        Assert.True(feed.Hear("boss_roar", 0, new Vector3(60, 0, 0), listener)); // a roar carries 80 m
        Assert.Equal(2, feed.Count);
        Assert.True(feed[1].Positioned);
        Assert.Equal(new Vector3(60, 0, 0), feed[1].Source);
    }

    [Fact]
    public void ARepeatedSoundRefreshesItsLineAndMovesItToTheBottom()
    {
        var feed = new CaptionFeed(Config());
        feed.Hear("hit_squish", 0, Vector3.UnitX, Vector3.Zero);
        feed.Hear("npc_arrive", 0.5);
        int version = feed.Version;
        feed.Hear("hit_squish", 1, Vector3.UnitZ, Vector3.Zero);
        Assert.Equal(2, feed.Count);
        Assert.Equal("Resident arrives", feed[0].Text);
        Assert.Equal("Gel squelches", feed[1].Text);
        Assert.Equal(1, feed[1].Heard);
        Assert.Equal(Vector3.UnitZ, feed[1].Source);
        Assert.NotEqual(version, feed.Version);
    }

    [Fact]
    public void WhenFullTheLineHeardLongestAgoGivesWay()
    {
        var feed = new CaptionFeed(Config(maxLines: 3));
        feed.Hear("npc_arrive", 0);
        feed.Hear("thunder_near", 1);
        feed.Hear("hit_squish", 2, Vector3.UnitX, Vector3.Zero);
        feed.Hear("boss_roar", 3, Vector3.UnitX, Vector3.Zero);
        Assert.Equal(3, feed.Count);
        Assert.Equal(new[] { "Thunder cracks", "Gel squelches", "Boss roars" }, new[] { feed[0].Text, feed[1].Text, feed[2].Text });
    }

    [Fact]
    public void LinesFadeAndExpire()
    {
        var feed = new CaptionFeed(Config());
        feed.Hear("npc_arrive", 0);
        Assert.Equal(1f, feed.Alpha(0, 2.0));
        Assert.Equal(0.5f, feed.Alpha(0, 2.75), 3);
        feed.Expire(2.9);
        Assert.Equal(1, feed.Count);
        feed.Expire(3.1);
        Assert.Equal(0, feed.Count);
    }

    [Theory]
    [InlineData(0, 0, -5, 0f)] // ahead (Godot looks down -Z)
    [InlineData(5, 0, 0, 90f)] // right
    [InlineData(-5, 0, 0, -90f)] // left
    [InlineData(0, 3, 5, 180f)] // behind (height ignored)
    public void BearingIsRelativeToTheView(float x, float y, float z, float degrees)
    {
        float? bearing = CaptionFeed.Bearing(Vector3.Zero, new Vector3(0, -0.3f, -1), new Vector3(x, y, z));
        Assert.NotNull(bearing);
        float got = bearing!.Value * 180f / MathF.PI;
        if (degrees == 180f) got = MathF.Abs(got);
        Assert.Equal(degrees, got, 1);
    }

    [Fact]
    public void ASoundOverheadHasNoBearing()
    {
        Assert.Null(CaptionFeed.Bearing(Vector3.Zero, -Vector3.UnitZ, new Vector3(0.1f, 6, 0)));
        Assert.Null(CaptionFeed.Bearing(Vector3.Zero, -Vector3.UnitY, new Vector3(5, 0, 0)));
    }

    [Fact]
    public void BadCaptionsAreReported()
    {
        var bad = Config();
        bad.Sounds["odd"] = new CaptionDef { Text = "", Kind = "music", Range = -1 };
        var problems = bad.Validate(id => id != "odd");
        Assert.Contains(problems, p => p.Contains("odd") && p.Contains("text"));
        Assert.Contains(problems, p => p.Contains("music"));
        Assert.Contains(problems, p => p.Contains("range"));
        Assert.Contains(problems, p => p.Contains("no such sound"));
    }
}
