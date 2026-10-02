using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Index;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class RecipeLookupTests
{
    [Fact]
    public void WoodIsUsedInTheWorkBenchByHand()
    {
        using var g = TestGame.Create();
        var uses = g.World.RecipeLookup.Using(g.Content.Items.Get("wood"));
        var bench = Assert.Single(uses, v => v.Output.Id == "work_bench");
        Assert.Empty(bench.Stations);
        Assert.Equal("", bench.StationLine);
    }

    [Fact]
    public void IronPickaxeNamesItsAnvils()
    {
        using var g = TestGame.Create();
        var making = g.World.RecipeLookup.Making(g.Content.Items.Get("iron_pickaxe"));
        Assert.NotEmpty(making);
        Assert.Contains(making, v => v.StationLine.Contains("Iron Anvil or Lead Anvil"));
        Assert.DoesNotContain(making, v => v.StationLine.Contains('_'));
    }

    [Fact]
    public void GroupIngredientsReadAnyWood()
    {
        using var g = TestGame.Create();
        var all = g.World.Recipes.All.Select(g.World.RecipeLookup.View).ToList();
        Assert.Contains(all, v => v.Ingredients.Any(i => i.Label == "Any Wood" && i.Accepts.Count > 1));
    }

    [Fact]
    public void EveryRecipeStationHasAName()
    {
        using var g = TestGame.Create();
        foreach (var st in g.Content.Recipes.All.SelectMany(r => r.Stations).Distinct())
            Assert.False(string.IsNullOrWhiteSpace(StationNames.Describe(st, g.Content)), st);
    }

    [Fact]
    public void EveryConditionTypeDescribes()
    {
        using var g = TestGame.Create();
        var samples = new Dictionary<string, object>
        {
            ["biome"] = new { Any = new[] { "jungle" } }, ["layer"] = new { Any = new[] { "cavern" } }, ["time"] = new { Is = "night" },
            ["flag"] = new { Id = "defeated_eye_of_cthulhu" }, ["event"] = new { Id = "blood_moon" }, ["depth"] = new { Min = 10f },
            ["light"] = new { Max = 3f }, ["moon"] = new { Phases = new[] { 0 } }, ["nearLiquid"] = new { Liquid = "water" },
            ["chance"] = new { P = 0.25f }, ["coins"] = new { Min = 5000L }, ["maxLife"] = new { Min = 200f },
            ["resident"] = new { Id = "merchant" }, ["residents"] = new { Min = 3f }, ["hasItem"] = new { Item = "torch" },
            ["wearing"] = new { Item = "torch" }, ["difficulty"] = new { Min = 1f }, ["expert"] = new { Is = true },
            ["defense"] = new { Min = 10f }, ["evil"] = new { Is = "crimson" }, ["secretSeed"] = new { Is = "drunk" },
            ["weather"] = new { Any = new[] { "rain" } }, ["fishingPower"] = new { Min = 50f }, ["fishingLiquid"] = new { Any = new[] { "lava" } },
            ["anglerQuest"] = new { Fish = "torch" }, ["season"] = new { Is = "christmas" }, ["worldRing"] = new { Min = 100f },
            ["all"] = new { Of = Array.Empty<object>() }, ["any"] = new { Of = Array.Empty<object>() }, ["not"] = new { Of = Array.Empty<object>() },
        };
        foreach (var type in ConditionCompiler.Types)
        {
            Assert.True(samples.ContainsKey(type), $"add a sample for '{type}'");
            var text = ConditionDescriber.Describe(TypedSpec.Of(type, samples[type]), g.Content);
            if (type is "all" or "any") continue; // empty composites read as nothing
            Assert.False(string.IsNullOrWhiteSpace(text), type);
        }
        Assert.Equal("at night", ConditionDescriber.Describe(TypedSpec.Of("time", samples["time"]), g.Content));
        Assert.Equal("after the Eye of Cthulhu is defeated", ConditionDescriber.Describe(TypedSpec.Of("flag", samples["flag"]), g.Content));
        Assert.Equal("during a Blood Moon", ConditionDescriber.Describe(TypedSpec.Of("event", samples["event"]), g.Content));
        Assert.Equal("The Merchant lives in town", ConditionDescriber.Describe(TypedSpec.Of("resident", samples["resident"]), g.Content));
        Assert.Equal("carrying 50 silver", ConditionDescriber.Describe(TypedSpec.Of("coins", samples["coins"]), g.Content));
    }
}
