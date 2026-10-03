using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-12: Terraria happiness multipliers and the happiness dialogue.</summary>
public class TownHappinessTests
{
    private static TestGame Create()
    {
        var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Summon(TestGame g, TownNpcDef def, int dx = 0) =>
        g.World.Town.Summon(def, g.World.Navigator.NearestStandable(g.Floor + new Int3(dx, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    private static TownNpcDef Def(string id, params MoodEntry[] mood) => new() { Id = id, Names = new[] { "Al" }, Mood = mood };

    private static string HomeBiome(TestGame g) =>
        g.World.Session.ContextAt(g.Floor + Int3.Up).Biomes.First(b => g.Content.Biomes.Contains(b));

    [Fact]
    public void LovedBiomeAndHatedNeighbour()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var plain = Summon(g, Def("plain"));
        float baseFactor = commerce.PriceFactor(plain);
        g.World.Town.Dismiss(plain);

        var lover = Summon(g, Def("lover", new MoodEntry { Biome = HomeBiome(g), Score = 2 }, new MoodEntry { Neighbor = "guide", Score = -2 }));
        Assert.Equal(0.88f, commerce.PriceFactor(lover) / baseFactor, 3);
        Assert.Contains("I love living in the", commerce.DescribeHappiness(lover));
        Summon(g, g.Content.TownNpcs.Get("guide"), 2);
        Assert.Equal(0.88f * 1.12f, commerce.PriceFactor(lover) / baseFactor, 3);
        Assert.Contains("I can't stand being near Guide", commerce.DescribeHappiness(lover));
    }

    [Fact]
    public void CrowdingSolitudeAndThePrincess()
    {
        using var g = Create();
        var commerce = g.World.Commerce;
        var npc = Summon(g, Def("loner"));
        Assert.Contains(commerce.Happiness(npc).Reasons, r => r.Key == "solitude" && r.Multiplier == 0.95f);
        foreach (var id in new[] { "guide", "merchant", "nurse", "painter", "clothier" }) Summon(g, g.Content.TownNpcs.Get(id), 1);
        var crowded = commerce.Happiness(npc);
        Assert.Contains(crowded.Reasons, r => r.Key == "crowded" && MathF.Abs(r.Multiplier - 1.10f) < 1e-4f);
        Assert.DoesNotContain(crowded.Reasons, r => r.Key == "solitude");
        float before = crowded.Factor;
        Summon(g, g.Content.TownNpcs.Get("princess"), -1);
        var withPrincess = commerce.Happiness(npc);
        Assert.Contains(withPrincess.Reasons, r => r.Key == "princess");
        Assert.True(withPrincess.Factor < before * 1.05f); // loved by all, despite one more neighbour
        Assert.InRange(withPrincess.Factor, g.World.Town.Config.MinPriceFactor, g.World.Town.Config.MaxPriceFactor);
    }

    [Fact]
    public void LandmarkKeepersHaveNoMood()
    {
        using var g = Create();
        var keeper = Summon(g, g.Content.TownNpcs.Get("old_man"));
        Assert.Equal(1f, g.World.Commerce.PriceFactor(keeper));
    }
}
