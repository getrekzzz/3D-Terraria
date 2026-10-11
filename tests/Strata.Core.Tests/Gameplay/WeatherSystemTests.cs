using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Gameplay.Weather;
using Strata.Core.Persistence;
using Strata.Core.Simulation;
using Strata.Core.Tests.Support;
using Strata.Core.World;

namespace Strata.Core.Tests.Gameplay;

public class WeatherSystemTests
{
    private static Strata.Core.Content.TypedSpec Spec(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<Strata.Core.Content.TypedSpec>(json, Strata.Core.Content.ContentJson.Options)!;

    /// <summary>Runs whole in-game hours (the clock jumps, as when sleeping) and records the weather after each.</summary>
    private static List<(Precipitation, bool, bool, Vector2)> Hours(TestGame g, int hours)
    {
        var seen = new List<(Precipitation, bool, bool, Vector2)>();
        var clock = g.World.Session.Clock;
        for (int h = 0; h < hours; h++)
        {
            clock.Advance(GameClock.TicksPerHour);
            g.World.Weather.Tick();
            var s = g.World.Weather.State;
            seen.Add((s.Precipitation, s.Windy, s.Sandstorm, s.WindTarget));
        }
        return seen;
    }

    [Fact]
    public void ScheduleIsDeterministicPerSeed()
    {
        using var a = TestGame.Create(seed: 7);
        using var b = TestGame.Create(seed: 7);
        using var c = TestGame.Create(seed: 8);
        const int hours = 24 * 60;
        var ha = Hours(a, hours);
        Assert.Equal(ha, Hours(b, hours));
        Assert.NotEqual(ha, Hours(c, hours));
        // Over sixty days it rains sometimes, but not most of the time; some days are windy.
        int wet = ha.Count(x => x.Item1 != Precipitation.None);
        Assert.InRange(wet, 24, hours / 2);
        Assert.Contains(ha, x => x.Item2);
    }

    [Fact]
    public void WeatherDrivesMirroredEventsAndConditions()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var w = g.World.Weather;
        w.StartPrecipitation(Precipitation.Storm, hours: 1f);
        g.Ticks(2);
        Assert.True(g.World.Events.IsActive("rain"));
        Assert.True(g.World.Events.IsActive("thunderstorm"));
        var ctx = g.World.Session.ContextAt(g.Floor + new Strata.Core.Common.Int3(0, 1, 0));
        Assert.Contains("rain", ctx.Events);
        Assert.Equal(ctx.Layer is WorldLayer.Surface or WorldLayer.Sky ? LocalWeather.Storm : LocalWeather.Clear, ctx.Weather);

        var cond = ConditionCompiler.Compile(Spec("""{ "type": "weather", "any": ["rain", "storm"] }"""));
        var probe = new ConditionContext { Weather = LocalWeather.Storm };
        Assert.True(cond.Evaluate(probe));
        probe.Weather = LocalWeather.Clear;
        Assert.False(cond.Evaluate(probe));
        var windy = ConditionCompiler.Compile(Spec("""{ "type": "weather", "any": ["windy"] }"""));
        Assert.True(windy.Evaluate(new ConditionContext { Windy = true }));

        // The spell runs out: the events end; the strength eases away over the fade.
        g.World.Session.Clock.Advance(GameClock.TicksPerHour + 10);
        g.Ticks(1);
        Assert.Equal(Precipitation.None, w.State.Precipitation);
        Assert.False(g.World.Events.IsActive("rain"));
        Assert.False(g.World.Events.IsActive("thunderstorm"));
    }

    [Fact]
    public void LocalWeatherFollowsBiomeAndLayer()
    {
        var s = new WeatherState { Precipitation = Precipitation.Rain };
        Assert.Equal(LocalWeather.Rain, s.LocalAt(new[] { "forest" }, WorldLayer.Surface));
        Assert.Equal(LocalWeather.Snow, s.LocalAt(new[] { "tundra" }, WorldLayer.Surface));
        // No rain underground, in the caverns, the underworld or space.
        foreach (var layer in new[] { WorldLayer.Underground, WorldLayer.Cavern, WorldLayer.Underworld, WorldLayer.Space })
            Assert.Equal(LocalWeather.Clear, s.LocalAt(new[] { "forest" }, layer));
        s.Precipitation = Precipitation.Storm;
        Assert.Equal(LocalWeather.Blizzard, s.LocalAt(new[] { "tundra" }, WorldLayer.Surface));
        s.Sandstorm = true;
        Assert.Equal(LocalWeather.Sandstorm, s.LocalAt(new[] { "desert" }, WorldLayer.Surface));
        Assert.Equal(LocalWeather.Storm, s.LocalAt(new[] { "forest" }, WorldLayer.Surface));
    }

    [Fact]
    public void WeatherSurvivesSaveAndLoad()
    {
        var dir = TestWorlds.TempDir();
        using (var g = TestGame.Create(dir))
        {
            g.World.Weather.StartPrecipitation(Precipitation.Rain, hours: 3f, intensity: 0.6f);
            g.World.Weather.SetSandstorm(true, hours: 2f);
            g.Ticks(30);
            g.World.SaveAll();
        }
        var meta = new WorldSaveStore(dir).LoadMeta();
        Assert.NotNull(meta.Weather);
        var session = Strata.Core.Session.WorldSession.Load(TestPaths.Content, dir, new Strata.Core.Meshing.RenderTables(TestPaths.Content, _ => 0), new Strata.Core.Diagnostics.MemoryLog());
        using var loaded = new Strata.Core.Session.GameWorld(session, new Strata.Core.Streaming.StreamingSettings(), 1);
        var s = loaded.Weather.State;
        Assert.Equal(Precipitation.Rain, s.Precipitation);
        Assert.Equal(0.6f, s.Intensity, 3);
        Assert.True(s.Sandstorm && s.Windy);
        Assert.InRange(s.PrecipitationTicksLeft, 3 * GameClock.TicksPerHour - 40, 3 * GameClock.TicksPerHour);
    }

    [Fact]
    public void ShippedConfigNamesRealBiomesAndEvents()
    {
        var config = WeatherConfig.Load(new Strata.Core.Content.Loading.FileSystemDataSource(TestPaths.ProjectRoot));
        foreach (var b in config.SnowBiomes.Concat(config.SandBiomes)) Assert.True(TestPaths.Content.Biomes.Contains(b), b);
        foreach (var e in new[] { config.RainEvent, config.StormEvent, config.WindyEvent, config.SandstormEvent })
            Assert.Equal(Strata.Core.Content.Defs.EventPhase.Weather, TestPaths.Content.Events.Get(e).Phase);
        foreach (var preset in new[] { "low", "medium", "high", "ultra" })
        {
            var budget = config.Presentation.BudgetFor(preset);
            Assert.InRange(budget.Rain + budget.Snow + budget.Sand + budget.Splash, 1000, 15000);
        }
        Assert.True(config.Presentation.BudgetFor("low").Rain < config.Presentation.BudgetFor("ultra").Rain);
    }
}
