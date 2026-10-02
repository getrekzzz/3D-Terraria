using Strata.Core.Conditions;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Guide;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class JournalTownTests
{
    [Fact]
    public void ZoologistArrivesOnceTheBestiaryIsTenPercentDone()
    {
        using var g = TestGame.Create();
        var arrival = ConditionCompiler.CompileAll(g.Content.TownNpcs.Get("zoologist").Arrival);
        Assert.False(arrival.Evaluate(g.World.Town.TownContext(g.Player)));
        var j = g.Player.State.Journal;
        foreach (var entry in g.World.Journal.Bestiary.Entries.OfType<EnemyDef>().Take(g.World.Journal.Bestiary.Entries.Count / 3))
        {
            j.MarkSeen(entry.Id);
            for (int i = 0; i < 60; i++) j.AddKill(entry.Id);
        }
        Assert.True(j.BestiaryPercent >= 10f, $"{j.BestiaryPercent}");
        Assert.True(arrival.Evaluate(g.World.Town.TownContext(g.Player)));
    }

    [Fact]
    public void KillAndObtainConditionsReadTheJournal()
    {
        using var g = TestGame.Create();
        var ctx = g.World.Town.TownContext(g.Player);
        Assert.NotNull(ctx.Records);
        g.Player.State.Journal.AddKill("zombie");
        g.Player.State.Journal.MarkObtained("torch");
        Assert.Equal(1, ctx.Records!.Kills("zombie"));
        Assert.True(ctx.Records.Obtained("torch"));
    }

    [Fact]
    public void RescuedTriggerCountsFreedTownsfolk()
    {
        using var g = TestGame.Create();
        var any = GuideTrigger.Compile(TypedSpec.Of(AchievementSpecs.Rescued, new { Min = 2 }));
        var goblin = GuideTrigger.Compile(TypedSpec.Of(AchievementSpecs.Rescued, new { Resident = "goblin_tinkerer" }));
        var j = g.Player.State.Journal;
        j.Count(JournalSystem.Rescued + ":goblin_tinkerer");
        j.Count(JournalSystem.Rescued);
        Assert.True(goblin.Met(g.World, g.Player));
        Assert.False(any.Met(g.World, g.Player));
        j.Count(JournalSystem.Rescued);
        Assert.True(any.Met(g.World, g.Player));
    }
}
