using Strata.Core.Conditions;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Relevance-driven preloading (P1b-14): which enemies are likely near a player, and their models.</summary>
public class SpawnPreloadTests
{
    private static List<string> Likely(TestGame g, string biome, bool day) =>
        g.World.Spawner.LikelyEnemies(new ConditionContext
        {
            Biomes = new[] { biome },
            Layer = WorldLayer.Surface,
            IsDay = day,
            Hour = day ? 12f : 0f,
            PlayerMaxLife = 100,
        }).Select(e => e.Id).ToList();

    [Fact]
    public void ForestNightIncludesZombieAndDemonEye()
    {
        using var g = TestGame.Create();
        var likely = Likely(g, "forest", day: false);
        Assert.Contains("zombie", likely);
        Assert.Contains("demon_eye", likely);
    }

    [Fact]
    public void DesertContextIncludesDesertEnemies()
    {
        using var g = TestGame.Create();
        var likely = Likely(g, "desert", day: true);
        Assert.Contains("vulture", likely);
        Assert.Contains("sand_slime", likely);
        Assert.DoesNotContain("zombie", likely); // a night rule with no biome named
    }

    [Fact]
    public void AskingDoesNotChangeSpawning()
    {
        using var g = TestGame.Create();
        var before = g.World.Spawner.LikelyEnemies(g.Player);
        Assert.Equal(before.Select(e => e.Id), g.World.Spawner.LikelyEnemies(g.Player).Select(e => e.Id));
    }

    [Fact]
    public void WormBossModelNamesIncludeSegmentsAndTail()
    {
        using var g = TestGame.Create();
        var names = g.Content.Enemies.Get("eater_of_worlds").ModelNames();
        Assert.Equal(new[] { "eater_of_worlds_head", "eater_of_worlds_body", "eater_of_worlds_tail" }, names);
        Assert.Equal(new[] { "zombie" }, g.Content.Enemies.Get("zombie").ModelNames().Take(1));
    }
}
