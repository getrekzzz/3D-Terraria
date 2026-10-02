using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Streaming;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>P5-06a: bound NPCs appear unseen, cannot be hurt or counted, and are freed by talking (or powder).</summary>
public class RescueTests
{
    private static TestGame Create(string? dir = null)
    {
        var g = TestGame.Create(dir);
        g.World.Spawner.Enabled = false;
        g.World.Town.ArrivalsEnabled = false;
        return g;
    }

    private static TownNpc Bind(TestGame g, string id) =>
        g.World.Town.Bind(g.Content.TownNpcs.Get(id), g.World.Navigator.NearestStandable(g.Floor + new Int3(2, 1, 2), Strata.Core.Gameplay.Navigation.NavAgent.Humanoid, 3)!.Value);

    [Fact]
    public void SitesMustBeOutOfSight()
    {
        using var g = Create();
        var sites = new SiteFinder(g.World);
        var anywhere = TrueCondition.Instance;
        Assert.Null(sites.Accept(g.Floor + new Int3(4, 1, 4), anywhere, new ConditionContext(), g.Player)); // in plain view
        TestHouses.Build(g, g.Floor + new Int3(7, 0, 0));
        Assert.NotNull(sites.Accept(g.Floor + new Int3(5, 1, 2), anywhere, new ConditionContext(), g.Player, radius: 1)); // behind walls
    }

    [Fact]
    public void BoundNpcsNeedTheirRequirements()
    {
        using var g = Create();
        for (int i = 0; i < 20; i++) g.World.Town.UpdateRescues();
        Assert.Null(g.World.Town.Find("goblin_tinkerer")); // no Goblin Army defeated yet
    }

    [Fact]
    public void BoundNpcsDoNotCountAndCannotBeHurt()
    {
        using var g = Create();
        var goblin = Bind(g, "goblin_tinkerer");
        Assert.True(goblin.Bound);
        Assert.Equal("Bound Goblin", goblin.Title);
        Assert.False(goblin.CountsAsResident);
        Assert.DoesNotContain("goblin_tinkerer", g.World.Town.TownContext(g.Player).Residents);
        g.World.Town.Slay(goblin);
        Assert.True(goblin.Alive);
    }

    [Fact]
    public void TalkingFreesThemAndTheyMoveIn()
    {
        using var g = Create();
        var goblin = Bind(g, "goblin_tinkerer");
        g.Ticks(10);
        Assert.True(g.World.Town.BeginTalk(goblin, g.Player));
        Assert.False(goblin.Bound);
        Assert.Contains(TownRescue.FlagFor("goblin_tinkerer"), g.World.Session.Flags);
        Assert.EndsWith("the Goblin Tinkerer", goblin.Title);
        TestHouses.Build(g, g.Floor + new Int3(7, 0, 0));
        g.World.Town.CheckArrivals(force: true);
        Assert.NotNull(goblin.Home);
    }

    [Fact]
    public void TheTorturedSoulNeedsPurificationPowder()
    {
        using var g = Create();
        var soul = Bind(g, "tax_collector");
        Assert.Equal("Tortured Soul", soul.Title);
        Assert.False(g.World.Town.Rescue(soul, g.Player, out var why));
        Assert.Contains("Purification Powder", why);
        var powder = g.Content.Items.Get("purification_powder");
        g.Player.State.Inventory.Backpack.Add(new ItemStack(powder, 2));
        Assert.True(g.World.Town.Rescue(soul, g.Player, out _));
        Assert.Equal(1, g.Player.State.Inventory.Backpack.Count(powder));
        Assert.False(soul.Bound);
    }

    [Fact]
    public void BoundStateSurvivesSaving()
    {
        var dir = Path.Combine(Path.GetTempPath(), "strata-rescue-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var g = Create(dir))
            {
                Bind(g, "stylist");
                g.World.SaveAll();
            }
            var session = WorldSession.Load(TestPaths.Content, dir, new RenderTables(TestPaths.Content, _ => 0), new MemoryLog());
            using var world = new GameWorld(session, new StreamingSettings(), 1);
            Assert.True(world.Town.Find("stylist")!.Bound);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheBoundMechanicWaitsAtTheDungeonsMechanicRoom()
    {
        using var g = Create();
        var plan = g.World.Session.Generator.Plan;
        Assert.True(plan.Landmarks.ContainsKey(Strata.Core.WorldGen.Plan.LandmarkIds.DungeonMechanic));
        var rescue = g.Content.TownNpcs.Get("mechanic").Rescue!;
        Assert.Equal(Strata.Core.WorldGen.Plan.LandmarkIds.DungeonMechanic, rescue.Landmark);
        Assert.Equal("Bound Mechanic", rescue.Title);
        foreach (var id in new[] { "goblin_tinkerer", "mechanic", "wizard", "stylist", "golfer", "angler", "tavernkeep", "tax_collector" })
            Assert.NotNull(g.Content.TownNpcs.Get(id).Rescue);
    }
}
