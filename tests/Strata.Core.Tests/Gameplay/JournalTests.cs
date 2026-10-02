using System.Numerics;
using Strata.Core.Gameplay.Guide;
using Strata.Core.Persistence;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class JournalTests
{
    private static Strata.Core.Gameplay.Entities.Npc Spawn(TestGame g, string id, float dx = 3f) =>
        g.World.Npcs.Spawn(g.Content.Enemies.Get(id), g.Player.Position + new Vector3(dx, 0, 0))!;

    [Fact]
    public void KillCreditsNearbyPlayer()
    {
        using var g = TestGame.Create();
        g.World.Npcs.Kill(Spawn(g, "zombie"));
        var j = g.Player.State.Journal;
        Assert.Equal(1, j.Kills("zombie"));
        var b = g.World.Journal.Bestiary;
        Assert.Equal(BestiaryUnlock.Stats, b.Unlock(g.Content.Enemies.Get("zombie"), j));
        Assert.True(j.BestiaryPercent > 0);
    }

    [Fact]
    public void FarKillIsNotCredited()
    {
        using var g = TestGame.Create();
        var npc = Spawn(g, "zombie");
        npc.Position = g.Player.Position + new Vector3(200, 0, 0);
        g.World.Npcs.Kill(npc);
        Assert.Equal(0, g.Player.State.Journal.Kills("zombie"));
    }

    [Fact]
    public void SeenWithinRadius()
    {
        using var g = TestGame.Create();
        Spawn(g, "zombie", 5f);
        var far = Spawn(g, "demon_eye", 5f);
        far.Position = g.Player.Position + new Vector3(300, 0, 0);
        do g.World.Session.Clock.Advance();
        while (g.World.Session.Clock.Tick % JournalSystem.SeenInterval != 0);
        g.World.Journal.Tick();
        var j = g.Player.State.Journal;
        Assert.Contains("zombie", j.Seen);
        Assert.DoesNotContain("demon_eye", j.Seen);
        Assert.Equal(BestiaryUnlock.Seen, g.World.Journal.Bestiary.Unlock(g.Content.Enemies.Get("zombie"), j));
    }

    [Fact]
    public void CaughtCritterUnlocksEntry()
    {
        using var g = TestGame.Create();
        var j = g.Player.State.Journal;
        j.MarkCaught("bunny");
        Assert.Equal(BestiaryUnlock.Full, g.World.Journal.Bestiary.Unlock(g.Content.Enemies.Get("bunny"), j));
    }

    [Fact]
    public void PartsCreditParent()
    {
        using var g = TestGame.Create();
        var b = g.World.Journal.Bestiary;
        Assert.False(b.Contains("moon_lord_hand"));
        Assert.True(b.Contains("moon_lord"));
        g.World.Npcs.Kill(Spawn(g, "moon_lord_hand"));
        var j = g.Player.State.Journal;
        Assert.Equal(1, j.Kills("moon_lord_hand"));
        Assert.Equal(0, j.Kills("moon_lord"));
        Assert.Contains("moon_lord", j.Seen);
        Assert.Equal(1, b.Kills("moon_lord", j));
        Assert.NotEqual(BestiaryUnlock.None, b.Unlock(g.Content.Enemies.Get("moon_lord"), j));
    }

    [Fact]
    public void CraftAndPickupMarkObtained()
    {
        using var g = TestGame.Create();
        var recipe = g.World.Recipes.RecipesFor(g.Content.Items.Get("work_bench"))[0];
        var env = Strata.Core.Gameplay.Crafting.CraftingEnvironment.Around(g.World, g.Player);
        Assert.Equal(1, g.World.Craft(g.Player, recipe, env));
        var j = g.Player.State.Journal;
        Assert.True(j.HasCrafted("work_bench"));
        Assert.True(j.HasObtained("work_bench"));
        Assert.False(j.HasCrafted("wood")); // starting kit: obtained by the carried sweep, never crafted
        for (int i = 0; i < 60; i++) g.World.Tick();
        Assert.True(j.HasObtained("copper_pickaxe"));
    }

    [Fact]
    public void SaveRoundTripKeepsUnknownIds()
    {
        using var g = TestGame.Create();
        var j = g.Player.State.Journal;
        j.AddKill("zombie");
        j.AddKill("zombie");
        j.AddKill("removed_creature");
        j.MarkSeen("zombie");
        j.MarkMet("guide");
        j.MarkObtained("removed_item");
        j.MarkCrafted("work_bench");
        j.Count("mined:#ore", 3);
        j.Unlock("benched", 1234);
        var loaded = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content).Journal;
        Assert.Equal(2, loaded.Kills("zombie"));
        Assert.Equal(1, loaded.Kills("removed_creature"));
        Assert.Contains("removed_item", loaded.Obtained);
        Assert.True(loaded.HasMet("guide"));
        Assert.True(loaded.HasCrafted("work_bench"));
        Assert.Equal(3, loaded.Counter("mined:#ore"));
        Assert.Equal(1234, loaded.Achievements["benched"]);
    }

    /// <summary>A version-1 save written before the journal existed still loads, with an empty journal.</summary>
    [Fact]
    public void OldSaveWithoutJournalLoads()
    {
        using var g = TestGame.Create();
        const string v1 = """
        { "format": "strata-player", "version": 1, "name": "old", "life": 80, "mana": 20, "deaths": 3,
          "backpack": [ { "slot": 0, "item": "copper_pickaxe", "count": 1, "prefix": null } ],
          "armor": [], "accessories": [], "buffs": [], "spawnPoints": {} }
        """;
        var state = PlayerSave.Deserialize(v1).ToState(g.Content);
        Assert.Equal("old", state.Name);
        Assert.Equal(3, state.Deaths);
        Assert.Empty(state.Journal.KillCounts);
        Assert.Empty(state.Journal.Achievements);
        var again = PlayerSave.Deserialize(PlayerSave.From(state).Serialize()).ToState(g.Content);
        Assert.Empty(again.Journal.Obtained);
    }
}
