using System.Text.Json;
using System.Text.RegularExpressions;
using Strata.Core.Content.Loading;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class ProgressionAdvisorTests
{
    [Fact]
    public void NewWorldStartsWithTheWorkBench()
    {
        using var g = TestGame.Create();
        Assert.Equal("work_bench", g.World.Advisor.Next(g.Player)[0].Id);
        Assert.Contains("Work Bench", g.World.Advisor.Advice(g.Player));
        g.Player.State.Journal.MarkCrafted("work_bench");
        Assert.NotEqual("work_bench", g.World.Advisor.Next(g.Player)[0].Id);
        Assert.True(g.World.Advisor.IsDone("work_bench", g.Player));
    }

    [Fact]
    public void DefeatingTheEyeOpensTheEvilBoss()
    {
        using var g = TestGame.Create();
        var advisor = g.World.Advisor;
        var j = g.Player.State.Journal;
        foreach (var item in new[] { "work_bench", "furnace", "iron_anvil", "iron_pickaxe" }) j.MarkObtained(item);
        j.MarkCrafted("work_bench");
        g.World.Progression.Set("smelted_metal");
        g.World.Progression.Set("found_life_crystal");
        var before = advisor.Steps(g.Player).Single(s => s.Step.Id == "evil_boss");
        Assert.False(before.Available);
        g.World.Progression.Set("defeated_eye_of_cthulhu");
        var steps = advisor.Steps(g.Player);
        Assert.True(steps.Single(s => s.Step.Id == "eye_of_cthulhu").Done);
        Assert.True(steps.Single(s => s.Step.Id == "evil_boss").Available);
        Assert.Contains(advisor.Next(g.Player, 10), s => s.Id == "evil_boss");
        Assert.DoesNotContain(advisor.Next(g.Player, 10), s => s.Id == "eye_of_cthulhu");
    }

    [Fact]
    public void ValidatorRejectsAfterCycles()
    {
        var db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
            { "flags": [ { "id": "f" } ],
              "guideSteps": [ { "id": "a", "text": "A", "after": ["b"], "doneWhen": [ { "type": "flag", "id": "f" } ] },
                              { "id": "b", "text": "B", "after": ["a"], "doneWhen": [ { "type": "flag", "id": "f" } ] } ] }
            """) });
        Assert.Contains(db.Report.Errors, e => e.Message.Contains("guide step dependency cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void TipsNameRealInputActions()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.ProjectRoot, "data/config/input.json")));
        var actions = doc.RootElement.GetProperty("actions").EnumerateObject().Select(p => p.Name).ToHashSet();
        using var g = TestGame.Create();
        Assert.True(g.Content.GuideTips.Count >= 40);
        foreach (var tip in g.Content.GuideTips.All)
        {
            if (tip.Action != null) Assert.Contains(tip.Action, actions);
            foreach (Match m in Regex.Matches(tip.Text, @"\{key:([a-z0-9_]+)\}")) Assert.Contains(m.Groups[1].Value, actions);
        }
    }
}
