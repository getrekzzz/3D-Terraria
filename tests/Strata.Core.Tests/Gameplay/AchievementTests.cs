using Strata.Core.Content.Defs;
using Strata.Core.Content.Loading;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class AchievementTests
{
    private static List<string> Watch(TestGame g)
    {
        var unlocked = new List<string>();
        g.World.Achievements.Unlocked += (_, def) => unlocked.Add(def.Id);
        Second(g.World); // attaches the tracker to the player's journal
        return unlocked;
    }

    private static void Second(GameWorld w)
    {
        for (int i = 0; i < 60; i++) w.Tick();
    }

    [Fact]
    public void CraftingWorkBenchUnlocksBenchedOnce()
    {
        using var g = TestGame.Create();
        var unlocked = Watch(g);
        var bench = g.World.Recipes.RecipesFor(g.Content.Items.Get("work_bench"))[0];
        g.Player.State.Inventory.Backpack.Add(new Strata.Core.Gameplay.Items.ItemStack(g.Content.Items.Get("wood"), 20));
        var env = CraftingEnvironment.Around(g.World, g.Player);
        Assert.Equal(2, g.World.Craft(g.Player, bench, env, 2));
        Assert.Equal(1, unlocked.Count(id => id == "benched"));
        Assert.True(g.Player.State.Journal.Achieved("benched"));
    }

    [Fact]
    public void KillCountProgress()
    {
        using var g = TestGame.Create();
        Watch(g);
        var def = g.Content.Achievements.Get("pretty_in_pink");
        Assert.Equal((0L, 1L), g.World.Achievements.Progress(def, g.Player));
        var j = g.Player.State.Journal;
        j.AddKill("pinky");
        Assert.True(j.Achieved("pretty_in_pink"));
        Assert.Equal((1L, 1L), g.World.Achievements.Progress(def, g.Player));
        var bulldozer = g.Content.Achievements.Get("bulldozer");
        j.Count("mined", 3);
        Assert.Equal((3L, 10000L), g.World.Achievements.Progress(bulldozer, g.Player));
    }

    [Fact]
    public void FlagTriggerUnlocksOnSweep()
    {
        using var g = TestGame.Create();
        var unlocked = Watch(g);
        g.World.Progression.Set("defeated_eye_of_cthulhu");
        Second(g.World);
        Assert.Contains("eye_on_you", unlocked);
        var bolts = g.Content.Achievements.Get("buckets_of_bolts");
        g.World.Progression.Set("defeated_the_twins");
        Assert.Equal((1L, 3L), g.World.Achievements.Progress(bolts, g.Player));
    }

    [Fact]
    public void AchievementsPersistThroughSave()
    {
        using var g = TestGame.Create();
        Watch(g);
        g.Player.State.Journal.AddKill("tim");
        var loaded = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content);
        Assert.True(loaded.Journal.Achieved("there_are_some_who_call_him"));
    }

    [Fact]
    public void ValidatorRejectsUnknownItemInTrigger()
    {
        var db = ContentLoader.LoadFromTexts(new[] { ("test.json", """
            { "achievements": [ { "id": "bad", "name": "Bad", "description": "x", "trigger": { "type": "obtainItem", "item": "nope" } },
                                { "id": "shape", "name": "Shape", "description": "x", "trigger": { "type": "killEnemy" } } ] }
            """) });
        Assert.Contains(db.Report.Errors, e => e.Message.Contains("unknown item 'nope'", StringComparison.Ordinal));
        Assert.Contains(db.Report.Errors, e => e.Message.Contains("exactly one of enemy or tag", StringComparison.Ordinal));
    }

    [Fact]
    public void ShippedAchievementsAllCompile()
    {
        using var g = TestGame.Create();
        Assert.True(g.Content.Achievements.Count >= 40);
        foreach (var def in g.Content.Achievements.All)
            Assert.True(g.World.Achievements.Progress(def, g.Player).Target >= 1, def.Id);
        Assert.Contains(g.Content.Achievements.All, a => a.Category == AchievementCategory.Slayer);
    }
}
