using Strata.Core.Common;
using Strata.Core.Gameplay.Town;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>X-05: emotes follow mood, neighbours, events and sleep; residents emote over time and answer the player.</summary>
public class EmoteRulesTests
{
    private static EmoteInput Calm => new(1f, false, false, false, false, false, false, false, false, false);

    [Fact]
    public void DangerAndSleepComeFirst()
    {
        Assert.Equal("alarm", EmoteRules.Pick(Calm with { Danger = true, Sleeping = true }, 0.1f));
        Assert.Equal("sleepy", EmoteRules.Pick(Calm with { Sleeping = true, Party = true }, 0.1f));
    }

    [Fact]
    public void MoodAndNeighboursShow()
    {
        Assert.Equal("angry", EmoteRules.Pick(Calm with { HatedNear = true }, 0.2f));
        Assert.Equal("love", EmoteRules.Pick(Calm with { LovedNear = true }, 0.2f));
        Assert.Contains(EmoteRules.Pick(Calm with { Happiness = 1.2f }, 0.9f), new[] { "angry", "sad" });
        Assert.Contains(EmoteRules.Pick(Calm with { Happiness = 0.85f }, 0.9f), new[] { "happy", "music" });
        Assert.Equal("music", EmoteRules.Pick(Calm with { Party = true }, 0.1f));
        Assert.Equal("chat", EmoteRules.Pick(Calm with { NearOther = true }, 0.1f));
        Assert.Null(EmoteRules.Pick(Calm, 0.9f)); // nothing to say
        Assert.Contains(EmoteRules.Pick(Calm with { Pet = true }, 0.2f), new[] { "love", "happy" });
    }

    [Fact]
    public void RepliesMatchTheEmote()
    {
        Assert.Equal("idea", EmoteRules.Reply("question", 1f));
        Assert.Equal("love", EmoteRules.Reply("love", 1f));
        Assert.Equal("sad", EmoteRules.Reply("happy", 1.2f));
    }

    [Fact]
    public void ResidentsEmoteOverTimeAndAnswerThePlayer()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        var town = g.World.Town;
        var at = g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value;
        var guide = town.Summon(g.Content.TownNpcs.Get("guide"), at);
        var merchant = town.Summon(g.Content.TownNpcs.Get("merchant"), at);
        string? seen = null;
        town.Emoted += (_, e) => seen = e;
        Assert.True(town.PlayerEmote(g.Player, "question"));
        Assert.Equal("idea", seen);
        Assert.Equal(2, town.EmotesShown);
        Assert.False(town.PlayerEmote(g.Player, "not_an_emote"));
        for (int i = 0; i < 2000 && town.EmotesShown < 4; i++) town.UpdateEmotes(); // cooldowns are clock based
        Assert.Equal(2, town.EmotesShown); // both are still cooling down
        g.World.Session.Clock.Advance(town.Config.Emotes.CooldownMax + 1);
        for (int i = 0; i < 400 && town.EmotesShown < 3; i++) town.UpdateEmotes();
        Assert.True(town.EmotesShown >= 3);
    }
}
