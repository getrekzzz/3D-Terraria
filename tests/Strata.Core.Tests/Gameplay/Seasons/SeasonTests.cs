using Strata.Core.Conditions;
using Strata.Core.Content.Loading;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Seasons;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay.Seasons;

public class SeasonTests
{
    private static readonly SeasonConfig Cfg = new();

    [Theory]
    [InlineData(10, 9, "")]
    [InlineData(10, 10, "halloween")]
    [InlineData(10, 31, "halloween")]
    [InlineData(11, 1, "halloween")]
    [InlineData(11, 2, "")]
    [InlineData(12, 14, "")]
    [InlineData(12, 15, "christmas")]
    [InlineData(12, 31, "christmas")]
    [InlineData(1, 1, "")]
    [InlineData(6, 15, "")]
    public void DateWindowsAreInclusive(int month, int day, string expected) =>
        Assert.Equal(expected, Cfg.SeasonOn(new DateTime(2026, month, day, 23, 59, 0)));

    [Fact]
    public void ShippedConfigMatchesTheDefaults()
    {
        var shipped = SeasonConfig.Load(new FileSystemDataSource(TestPaths.ProjectRoot));
        Assert.Equal(Cfg.Seasons.Select(s => (s.Id, s.From[0], s.From[1], s.To[0], s.To[1])), shipped.Seasons.Select(s => (s.Id, s.From[0], s.From[1], s.To[0], s.To[1])));
    }

    [Fact]
    public void WindowsMayWrapPastNewYear()
    {
        var cfg = new SeasonConfig { Seasons = new[] { new SeasonWindow { Id = "winter", From = new[] { 12, 20 }, To = new[] { 1, 5 } } } };
        Assert.Equal("winter", cfg.SeasonOn(new DateTime(2026, 12, 25)));
        Assert.Equal("winter", cfg.SeasonOn(new DateTime(2027, 1, 5)));
        Assert.Equal("", cfg.SeasonOn(new DateTime(2027, 1, 6)));
    }

    [Fact]
    public void TheClockSettingAndOverrideDecide()
    {
        var s = new SeasonState { Clock = () => new DateTime(2026, 10, 20) };
        Assert.Equal("halloween", s.Current);
        s.Clock = () => new DateTime(2026, 3, 1);
        Assert.Equal("", s.Current);
        s.Override = "christmas";
        Assert.Equal("christmas", s.Current);
        s.Clock = () => new DateTime(2026, 10, 20);
        s.Override = "off";
        Assert.Equal("", s.Current); // forced off even in the window
        s.Override = null;
        Assert.Equal("halloween", s.Current);
        bool enabled = false;
        s.Enabled = () => enabled;
        s.Invalidate();
        Assert.Equal("", s.Current); // the player switched seasons off
        s.Override = "halloween";
        Assert.Equal("halloween", s.Current); // the console still forces one
    }

    [Fact]
    public void TheSeasonConditionReadsTheWorld()
    {
        using var g = TestGame.Create();
        var cond = ConditionCompiler.Compile(new Strata.Core.Content.TypedSpec("season", System.Text.Json.JsonDocument.Parse("{\"type\":\"season\",\"is\":\"halloween\"}").RootElement));
        g.World.Session.Seasons.Override = "halloween";
        Assert.True(cond.Evaluate(g.World.Session.ContextAt(g.Floor)));
        g.World.Session.Seasons.Override = "off";
        Assert.False(cond.Evaluate(g.World.Session.ContextAt(g.Floor)));
    }

    [Fact]
    public void EnemiesDropPresentsOnlyAtChristmas()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var slime = g.Content.Enemies.Get("green_slime");
        var present = g.Content.Items.Get("present");
        int Presents(string season)
        {
            g.World.Session.Seasons.Override = season;
            int before = g.World.Drops.Items.Count(d => d.Stack.Item == present);
            for (int i = 0; i < 150; i++)
            {
                var npc = g.World.Npcs.Spawn(slime, g.Player.Position + new System.Numerics.Vector3(3, 0.1f, 3))!;
                g.World.Npcs.Kill(npc);
                g.World.Session.Clock.Advance(1);
            }
            return g.World.Drops.Items.Count(d => d.Stack.Item == present) - before;
        }
        Assert.Equal(0, Presents("off"));
        Assert.Equal(0, Presents("halloween"));
        Assert.InRange(Presents("christmas"), 2, 40);
    }

    [Fact]
    public void BagsOpenIntoSeasonalLoot()
    {
        using var g = TestGame.Create();
        var roller = new LootRoller(g.Content);
        var rng = new Pcg32(4);
        var seen = new HashSet<string>();
        for (int i = 0; i < 100; i++)
            foreach (var s in roller.Roll("goodie_bag", ref rng, new ConditionContext())) seen.Add(s.Item!.Id);
        Assert.Contains("witch_hat", seen);
        Assert.Contains("jack_o_lantern", seen);
        seen.Clear();
        for (int i = 0; i < 100; i++)
            foreach (var s in roller.Roll("present", ref rng, new ConditionContext())) seen.Add(s.Item!.Id);
        Assert.Contains("santa_hat", seen);
        Assert.DoesNotContain("snow_globe", seen); // hardmode only
    }

    [Fact]
    public void SantaVisitsOnlyAtChristmas()
    {
        using var g = TestGame.Create();
        var santa = g.Content.TownNpcs.Get("santa_claus");
        var arrival = ConditionCompiler.CompileAll(santa.Arrival);
        var leaves = ConditionCompiler.CompileAll(santa.LeavesWhen);
        g.World.Progression.Set("defeated_frost_legion");
        g.World.Session.Seasons.Override = "christmas";
        var ctx = g.World.Town.TownContext(g.Player);
        Assert.True(arrival.Evaluate(ctx));
        Assert.False(leaves.Evaluate(ctx));
        g.World.Session.Seasons.Override = "off";
        ctx = g.World.Town.TownContext(g.Player);
        Assert.False(arrival.Evaluate(ctx));
        Assert.True(leaves.Evaluate(ctx));
    }
}
