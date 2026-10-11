using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Weather;

/// <summary>
/// Weather tuning (data/config/weather.json). One in-game hour is one real minute; rain spells, windy days and
/// sandstorms are rolled once per in-game hour from the world seed, so a world's weather is deterministic.
/// </summary>
public sealed class WeatherConfig
{
    public const string Path = "data/config/weather.json";

    /// <summary>Chance per in-game hour that a rain spell starts while the sky is dry.</summary>
    public float RainChancePerHour { get; init; } = 0.035f;

    /// <summary>Length of a rain spell in in-game hours [min, max].</summary>
    public float[] RainHours { get; init; } = { 2f, 9f };

    /// <summary>Share of rain spells that are thunderstorms.</summary>
    public float StormShare { get; init; } = 0.3f;

    public float[] RainIntensity { get; init; } = { 0.35f, 0.8f };
    public float[] StormIntensity { get; init; } = { 0.8f, 1f };

    /// <summary>Chance per dawn that the day turns windy.</summary>
    public float WindyChancePerDay { get; init; } = 0.12f;

    /// <summary>Hour of the day the windy-day roll happens.</summary>
    public int WindyRollHour { get; init; } = 6;

    public float[] WindyHours { get; init; } = { 6f, 14f };

    /// <summary>Wind speed (0 still … 1 gale) on a calm hour and on a windy day [min, max].</summary>
    public float[] CalmWind { get; init; } = { 0.05f, 0.3f };
    public float[] WindyWind { get; init; } = { 0.55f, 0.95f };

    /// <summary>A thunderstorm's wind never drops below this.</summary>
    public float StormWindMin { get; init; } = 0.6f;

    /// <summary>Chance per in-game hour of a windy day that a sandstorm blows up over the deserts.</summary>
    public float SandstormChancePerHour { get; init; } = 0.12f;

    public float[] SandstormHours { get; init; } = { 1f, 4f };

    /// <summary>Seconds for precipitation to build up or die away (presentation and wetness follow it).</summary>
    public float FadeSeconds { get; init; } = 20f;

    /// <summary>Seconds for the wind to settle on a new hour's speed and heading.</summary>
    public float WindEaseSeconds { get; init; } = 30f;

    /// <summary>Rain falls as snow over these biomes (a thunderstorm as a blizzard).</summary>
    public string[] SnowBiomes { get; init; } = { "tundra", "ice_biome" };

    /// <summary>Sandstorms blow over these biomes.</summary>
    public string[] SandBiomes { get; init; } = { "desert" };

    /// <summary>World events that mirror the weather (announcements, spawn rules, music): rain, storm, windy, sandstorm.</summary>
    public string RainEvent { get; init; } = "rain";
    public string StormEvent { get; init; } = "thunderstorm";
    public string WindyEvent { get; init; } = "windy_day";
    public string SandstormEvent { get; init; } = "sandstorm";

    /// <summary>Presentation: particle budgets per graphics preset and the storm's lightning rhythm.</summary>
    public WeatherPresentation Presentation { get; init; } = new();

    public static WeatherConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<WeatherConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}

/// <summary>How the engine draws the weather; budgets are the most particles alive per system at full intensity.</summary>
public sealed class WeatherPresentation
{
    public Dictionary<string, WeatherBudget> Budgets { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["low"] = new WeatherBudget { Rain = 1200, Snow = 700, Sand = 700, Splash = 160 },
        ["medium"] = new WeatherBudget { Rain = 2600, Snow = 1400, Sand = 1500, Splash = 320 },
        ["high"] = new WeatherBudget { Rain = 4500, Snow = 2400, Sand = 2500, Splash = 600 },
        ["ultra"] = new WeatherBudget { Rain = 6500, Snow = 3200, Sand = 3500, Splash = 900 },
    };

    /// <summary>Seconds between lightning strikes in a full thunderstorm [min, max].</summary>
    public float[] LightningSeconds { get; init; } = { 5f, 16f };

    /// <summary>Thunder lags the flash by distance: [min, max] seconds.</summary>
    public float[] ThunderDelay { get; init; } = { 0.3f, 2.6f };

    public WeatherBudget BudgetFor(string preset) =>
        Budgets.TryGetValue(preset, out var b) ? b : Budgets.TryGetValue("medium", out var m) ? m : new WeatherBudget();
}

public sealed class WeatherBudget
{
    public int Rain { get; init; } = 2600;
    public int Snow { get; init; } = 1400;
    public int Sand { get; init; } = 1500;
    public int Splash { get; init; } = 320;
}
