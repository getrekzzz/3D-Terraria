using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Simulation;

namespace Strata.Core.Tests.Content;

/// <summary>P5-03: rescued, hasItem category, bestiary, season, angler quests, killed/obtained/achieved.</summary>
public class TownConditionTests
{
    private static ICondition C(string json) =>
        ConditionCompiler.Compile(System.Text.Json.JsonSerializer.Deserialize<TypedSpec>(json, ContentJson.Options)!);

    private sealed class FakeRecords : IPlayerRecords
    {
        public float BestiaryPercent { get; init; }
        public int Kills(string enemy) => enemy == "zombie" ? 3 : 0;
        public bool Obtained(string item) => item == "dirt_block";
        public bool Achieved(string id) => id == "timber";
    }

    [Fact]
    public void RescuedReadsTheRescueFlag()
    {
        var c = C("""{"type":"rescued","id":"goblin_tinkerer"}""");
        var ctx = new ConditionContext();
        Assert.False(c.Evaluate(ctx));
        ctx.Flags = new HashSet<string> { "rescued_goblin_tinkerer" };
        Assert.True(c.Evaluate(ctx));
    }

    [Fact]
    public void HasItemCountsACategory()
    {
        var c = C("""{"type":"hasItem","category":"dye"}""");
        var ctx = new ConditionContext { ItemCount = key => key == "@dye" ? 1 : 0 };
        Assert.True(c.Evaluate(ctx));
        ctx.ItemCount = _ => 0;
        Assert.False(c.Evaluate(ctx));
    }

    [Fact]
    public void BestiaryNeedsRecords()
    {
        var c = C("""{"type":"bestiary","min":10}""");
        Assert.False(c.Evaluate(new ConditionContext()));
        Assert.False(c.Evaluate(new ConditionContext { Records = new FakeRecords { BestiaryPercent = 9.9f } }));
        Assert.True(c.Evaluate(new ConditionContext { Records = new FakeRecords { BestiaryPercent = 10f } }));
    }

    [Fact]
    public void SeasonAnglerAndJournalFacts()
    {
        var ctx = new ConditionContext { Season = "christmas", AnglerQuests = 5, Records = new FakeRecords() };
        Assert.True(C("""{"type":"season","is":"christmas"}""").Evaluate(ctx));
        Assert.False(C("""{"type":"season","is":"none"}""").Evaluate(ctx));
        Assert.True(C("""{"type":"season","is":"none"}""").Evaluate(new ConditionContext()));
        Assert.True(C("""{"type":"anglerQuests","min":5}""").Evaluate(ctx));
        Assert.False(C("""{"type":"anglerQuests","min":10}""").Evaluate(ctx));
        Assert.True(C("""{"type":"killed","enemy":"zombie","min":3}""").Evaluate(ctx));
        Assert.False(C("""{"type":"killed","enemy":"zombie","min":4}""").Evaluate(ctx));
        Assert.True(C("""{"type":"obtained","item":"dirt_block"}""").Evaluate(ctx));
        Assert.True(C("""{"type":"achieved","id":"timber"}""").Evaluate(ctx));
        Assert.False(C("""{"type":"killed","enemy":"zombie"}""").Evaluate(new ConditionContext()));
    }

    [Theory]
    [InlineData(12, 14, "")]
    [InlineData(12, 15, "christmas")]
    [InlineData(12, 31, "christmas")]
    [InlineData(10, 9, "")]
    [InlineData(10, 31, "halloween")]
    [InlineData(11, 1, "halloween")]
    [InlineData(11, 2, "")]
    public void SeasonCalendarFollowsTheDate(int month, int day, string season) =>
        Assert.Equal(season, SeasonCalendar.Of(new DateOnly(2026, month, day)));
}
