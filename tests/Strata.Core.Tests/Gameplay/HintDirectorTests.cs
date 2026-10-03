using Strata.Core.Content.Defs;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class HintDirectorTests
{
    private static List<(string Id, long Second)> Watch(TestGame g, Func<long> second)
    {
        var shown = new List<(string, long)>();
        g.World.Hints.Shown += (_, def) => shown.Add((def.Id, second()));
        return shown;
    }

    private static void Seconds(GameWorld w, int n)
    {
        for (int i = 0; i < n * 60; i++) w.Tick();
    }

    [Fact]
    public void HintShowsOnceAndIsRemembered()
    {
        using var g = TestGame.Create();
        g.World.Session.Clock.SetHour(10f);
        var shown = Watch(g, () => 0);
        Seconds(g.World, 5);
        Assert.Equal(new[] { "move" }, shown.Select(s => s.Id)); // highest priority once the start delay passes
        Assert.Contains("move", g.Player.State.Journal.HintsShown);
        Seconds(g.World, 200);
        Assert.True(shown.Count > 1);
        Assert.Equal(shown.Count, shown.Select(s => s.Id).Distinct().Count()); // each at most once
    }

    [Fact]
    public void CooldownSpacesHints()
    {
        using var g = TestGame.Create();
        g.World.Session.Clock.SetHour(22f); // night, no house: night_danger is due along with move
        long sec = 0;
        var shown = Watch(g, () => sec);
        for (sec = 0; sec < 80; sec++) Seconds(g.World, 1);
        Assert.True(shown.Count >= 2, string.Join(",", shown));
        Assert.Equal("move", shown[0].Id); // highest priority first
        for (int i = 1; i < shown.Count; i++)
            Assert.True(shown[i].Second - shown[i - 1].Second >= g.World.Hints.CooldownSeconds - 1, string.Join(",", shown));
    }

    [Fact]
    public void DisabledShowsNothing()
    {
        using var g = TestGame.Create();
        bool? changed = null;
        g.World.Hints.EnabledChanged += on => changed = on;
        g.World.Hints.Enabled = false;
        Assert.False(changed);
        var shown = Watch(g, () => 0);
        Seconds(g.World, 60);
        Assert.Empty(shown);
        Assert.Empty(g.Player.State.Journal.HintsShown);
        g.World.Hints.Enabled = true;
        Seconds(g.World, 2);
        Assert.Single(shown);
    }

    [Fact]
    public void ConditionsGateHints()
    {
        using var g = TestGame.Create();
        var hardmode = g.Content.Hints.Get("hardmode");
        Assert.False(g.World.Hints.IsDue(g.Player, hardmode));
        g.World.Progression.Set("hardmode");
        Assert.True(g.World.Hints.IsDue(g.Player, hardmode));
        var night = g.Content.Hints.Get("night_shelter");
        g.World.Session.Clock.SetHour(10f);
        Assert.False(g.World.Hints.IsDue(g.Player, night));
        g.World.Session.Clock.SetHour(18f);
        Assert.True(g.World.Hints.IsDue(g.Player, night));
    }

    [Fact]
    public void ShownHintsSurviveSaveRoundTrip()
    {
        using var g = TestGame.Create();
        g.World.Hints.Force(g.Player, g.Content.Hints.Get("chop_tree"));
        var state = PlayerSave.Deserialize(PlayerSave.From(g.Player.State).Serialize()).ToState(g.Content);
        Assert.Contains("chop_tree", state.Journal.HintsShown);
        g.World.Hints.Reset(g.Player);
        Assert.Empty(g.Player.State.Journal.HintsShown);
    }

    [Fact]
    public void EveryHintCompilesAndFormats()
    {
        using var g = TestGame.Create();
        Assert.True(g.Content.Hints.Count >= 12);
        foreach (HintDef h in g.Content.Hints.All) g.World.Hints.IsDue(g.Player, h); // no throw
        Assert.DoesNotContain(g.Log.Entries, e => e.Message.StartsWith("hint "));
    }
}
