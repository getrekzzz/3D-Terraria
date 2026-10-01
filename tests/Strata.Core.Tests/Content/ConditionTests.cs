using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.World;

namespace Strata.Core.Tests.Content;

public class ConditionTests
{
    private static TypedSpec Spec(string json)
    {
        var el = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        return System.Text.Json.JsonSerializer.Deserialize<TypedSpec>(el.GetRawText(), ContentJson.Options)!;
    }

    [Fact]
    public void BiomeLayerAndTimeCombine()
    {
        var cond = ConditionCompiler.CompileAll(new[]
        {
            Spec("""{ "type": "biome", "any": ["forest"], "none": ["corruption"] }"""),
            Spec("""{ "type": "layer", "any": ["surface"] }"""),
            Spec("""{ "type": "time", "is": "night" }"""),
        });
        var ctx = new ConditionContext { Biomes = new[] { "forest" }, Layer = WorldLayer.Surface, IsDay = false };
        Assert.True(cond.Evaluate(ctx));
        ctx.IsDay = true;
        Assert.False(cond.Evaluate(ctx));
        ctx.IsDay = false;
        ctx.Biomes = new[] { "forest", "corruption" };
        Assert.False(cond.Evaluate(ctx));
    }

    [Fact]
    public void NestedCompositesWork()
    {
        var cond = ConditionCompiler.Compile(Spec("""
            { "type": "any", "of": [
                { "type": "flag", "id": "hardmode" },
                { "type": "not", "of": [ { "type": "depth", "max": 100 } ] }
            ] }
            """));
        var ctx = new ConditionContext { Depth = 50, Flags = new HashSet<string>() };
        Assert.False(cond.Evaluate(ctx));
        ctx.Depth = 150;
        Assert.True(cond.Evaluate(ctx));
        ctx.Depth = 50;
        ctx.Flags = new HashSet<string> { "hardmode" };
        Assert.True(cond.Evaluate(ctx));
    }

    [Fact]
    public void HourWindowWrapsMidnight()
    {
        var cond = ConditionCompiler.Compile(Spec("""{ "type": "time", "fromHour": 22, "toHour": 3 }"""));
        Assert.True(cond.Evaluate(new ConditionContext { Hour = 23 }));
        Assert.True(cond.Evaluate(new ConditionContext { Hour = 1 }));
        Assert.False(cond.Evaluate(new ConditionContext { Hour = 12 }));
    }

    [Fact]
    public void ExpertConditionExcludesClassicAndJourney()
    {
        var expert = ConditionCompiler.Compile(Spec("""{ "type": "expert" }"""));
        var notExpert = ConditionCompiler.Compile(Spec("""{ "type": "expert", "is": false }"""));
        foreach (var (d, want) in new[] { (Difficulty.Classic, false), (Difficulty.Expert, true), (Difficulty.Master, true), (Difficulty.Journey, false) })
        {
            var ctx = new ConditionContext { Difficulty = (int)d };
            Assert.Equal(want, expert.Evaluate(ctx));
            Assert.Equal(!want, notExpert.Evaluate(ctx));
        }
    }

    [Fact]
    public void DefenseConditionReadsPlayerDefense()
    {
        var cond = ConditionCompiler.Compile(Spec("""{ "type": "defense", "min": 10, "max": 40 }"""));
        Assert.False(cond.Evaluate(new ConditionContext { PlayerDefense = 9 }));
        Assert.True(cond.Evaluate(new ConditionContext { PlayerDefense = 10 }));
        Assert.True(cond.Evaluate(new ConditionContext { PlayerDefense = 40 }));
        Assert.False(cond.Evaluate(new ConditionContext { PlayerDefense = 41 }));
    }
}
