using Strata.Core.Presentation;

namespace Strata.Core.Tests.Presentation;

public class ChatHistoryTests
{
    private static readonly Rgb Purple = new(175, 75, 255);

    [Fact]
    public void KeepsTheNewestFiftyOldestFirst()
    {
        var log = new ChatHistory();
        for (int i = 0; i < 60; i++) log.Add($"line {i}", Purple, i);
        Assert.Equal(ChatHistory.Capacity, log.Count);
        Assert.Equal(60, log.Added);
        Assert.Equal("line 10", log[0].Text);
        Assert.Equal("line 59", log[log.Count - 1].Text);
        Assert.Equal(Purple, log[0].Color);
        Assert.Throws<ArgumentOutOfRangeException>(() => log[log.Count]);
    }

    [Fact]
    public void LinesHoldTenSecondsThenFadeOverOne()
    {
        Assert.Equal(1f, ChatHistory.Alpha(0));
        Assert.Equal(1f, ChatHistory.Alpha(ChatHistory.Hold));
        Assert.Equal(0.5f, ChatHistory.Alpha(ChatHistory.Hold + ChatHistory.Fade / 2), 3);
        Assert.Equal(0f, ChatHistory.Alpha(ChatHistory.Hold + ChatHistory.Fade));
        Assert.Equal(0f, ChatHistory.Alpha(100));
    }

    [Fact]
    public void TheLogShowsTheLastTenLinesNotYetFaded()
    {
        var log = new ChatHistory();
        Assert.Equal(0, log.FirstShown(0));
        log.Add("Eye of Cthulhu has awoken!", Purple, 0);
        log.Add("The Guide has arrived!", new Rgb(50, 125, 255), 5);
        Assert.Equal(0, log.FirstShown(6));
        Assert.Equal(1, log.FirstShown(11.5)); // the first line has faded out, the second still shows
        Assert.Equal(log.Count, log.FirstShown(20));
        for (int i = 0; i < 12; i++) log.Add($"line {i}", Purple, 30);
        Assert.Equal(log.Count - ChatHistory.Shown, log.FirstShown(30));
    }
}
