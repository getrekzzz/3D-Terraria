using Strata.Core.Gameplay.Weather;

namespace Strata.Core.Commands;

/// <summary><c>weather</c>: shows or sets the weather (probes in <see cref="WeatherProbes"/>).</summary>
public static class WeatherCommands
{
    public static void Register(CommandRegistry r)
    {
        r.Register(new CommandDef
        {
            Name = "weather", Usage = "weather [clear | rain | storm | snow | blizzard | sandstorm | windy | calm] [hours]",
            Help = "Shows the weather, or sets it: rain/storm everywhere; snow/blizzard/sandstorm also force that look on every outdoor place.",
            Run = (ctx, a) =>
            {
                var w = ctx.World.Weather;
                var s = w.State;
                float hours = a.Count > 1 ? a.Float(1, "hours") : 0f;
                switch (a.OptStr(0))
                {
                    case null:
                        var here = ctx.World.Session.ContextAt(ctx.Player.FeetCell).Weather;
                        return CommandResult.Success($"{s.Precipitation} (intensity {s.Current:0.00} of {s.Intensity:0.00}, {s.PrecipitationTicksLeft / 60}s left), "
                            + $"wind {s.WindSpeed:0.00}{(s.Windy ? " windy" : "")}{(s.Sandstorm ? ", sandstorm" : "")}; here: {here}");
                    case "clear":
                        s.Forced = null;
                        w.StartPrecipitation(Precipitation.None);
                        w.SetSandstorm(false);
                        s.Current = 0f;
                        return CommandResult.Success("The sky clears.");
                    case "rain" or "storm" or "snow" or "blizzard":
                        string kind = a[0];
                        bool storm = kind is "storm" or "blizzard";
                        s.Forced = kind switch { "snow" => LocalWeather.Snow, "blizzard" => LocalWeather.Blizzard, _ => null };
                        w.StartPrecipitation(storm ? Precipitation.Storm : Precipitation.Rain, hours);
                        s.Current = s.Intensity; // at full strength now (no build-up) for scripts and screenshots
                        return CommandResult.Success($"Weather: {kind} ({s.Intensity:0.00}).");
                    case "sandstorm":
                        s.Forced = LocalWeather.Sandstorm;
                        w.SetSandstorm(true, hours);
                        return CommandResult.Success("A sandstorm blows.");
                    case "windy" or "calm":
                        w.SetWindy(a[0] == "windy", hours);
                        return CommandResult.Success($"Wind {s.WindSpeed:0.00}.");
                    default:
                        throw new CommandException("weather clear|rain|storm|snow|blizzard|sandstorm|windy|calm [hours]");
                }
            },
        });
    }
}

/// <summary>
/// Weather probes: <c>weather.kind</c> (0 dry, 1 rain, 2 storm), <c>weather.local</c> (the <see cref="LocalWeather"/>
/// at the player), <c>weather.intensity</c> (eased strength), <c>weather.wind</c> (speed 0–1), <c>weather.windy</c>,
/// <c>weather.sandstorm</c>.
/// </summary>
[ProbeSource]
internal static class WeatherProbes
{
    public static void Register(CommandContext ctx)
    {
        var s = ctx.World.Session;
        ctx.AddProbe("weather.kind", () => (int)s.Weather.Precipitation);
        ctx.AddProbe("weather.local", () => (int)s.ContextAt(ctx.Player.FeetCell).Weather);
        ctx.AddProbe("weather.intensity", () => s.Weather.Current);
        ctx.AddProbe("weather.wind", () => s.Weather.WindSpeed);
        ctx.AddProbe("weather.windy", () => s.Weather.Windy ? 1 : 0);
        ctx.AddProbe("weather.sandstorm", () => s.Weather.Sandstorm ? 1 : 0);
    }
}
