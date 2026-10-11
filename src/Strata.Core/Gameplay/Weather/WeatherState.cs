using System.Numerics;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Weather;

/// <summary>What falls from the sky world-wide (where it lands as snow or not at all depends on the place).</summary>
public enum Precipitation
{
    None,
    Rain,
    Storm,
}

/// <summary>The weather a place actually has: rain turns to snow over the tundra and never reaches underground.</summary>
public enum LocalWeather
{
    Clear,
    Rain,
    Storm,
    Snow,
    Blizzard,
    Sandstorm,
}

/// <summary>
/// The world's weather right now (saved in world.json): precipitation and its strength, the wind, windy days and
/// sandstorms. <see cref="WeatherSystem"/> schedules it; conditions, spawning, fishing and the engine read it.
/// </summary>
public sealed class WeatherState
{
    public WeatherConfig Config { get; set; } = new();

    public Precipitation Precipitation { get; set; }

    /// <summary>Strength of the current spell when fully built up (0–1).</summary>
    public float Intensity { get; set; }

    /// <summary>Strength now, easing in when a spell starts and out after it ends (0–1).</summary>
    public float Current { get; set; }

    public int PrecipitationTicksLeft { get; set; }

    public bool Windy { get; set; }
    public int WindyTicksLeft { get; set; }

    public bool Sandstorm { get; set; }
    public int SandstormTicksLeft { get; set; }

    /// <summary>Wind now, on the ground plane (x, z); its length is the speed, 0 still … 1 gale.</summary>
    public Vector2 Wind { get; set; } = new(0.15f, 0.05f);

    /// <summary>The wind this hour is settling towards.</summary>
    public Vector2 WindTarget { get; set; } = new(0.15f, 0.05f);

    /// <summary>The last in-game hour whose rolls have been made (−1 before the first).</summary>
    public long LastHour { get; set; } = -1;

    /// <summary>Debug override (the <c>weather snow</c> command): every outdoor place shows this kind. Not saved.</summary>
    public LocalWeather? Forced { get; set; }

    public float WindSpeed => Wind.Length();

    /// <summary>
    /// The weather at a place with these biomes on this layer: nothing underground or in space; snow and blizzards
    /// over snow biomes; sandstorms over sand biomes while one blows; otherwise the world's rain or storm.
    /// </summary>
    public LocalWeather LocalAt(IReadOnlyCollection<string> biomes, WorldLayer layer)
    {
        if (layer is not (WorldLayer.Surface or WorldLayer.Sky)) return LocalWeather.Clear;
        if (Forced is { } forced) return forced;
        if (Sandstorm && Any(biomes, Config.SandBiomes)) return LocalWeather.Sandstorm;
        if (Precipitation == Precipitation.None) return LocalWeather.Clear;
        bool snow = Any(biomes, Config.SnowBiomes);
        bool storm = Precipitation == Precipitation.Storm;
        return snow ? (storm ? LocalWeather.Blizzard : LocalWeather.Snow) : (storm ? LocalWeather.Storm : LocalWeather.Rain);
    }

    private static bool Any(IReadOnlyCollection<string> biomes, string[] set)
    {
        foreach (var b in biomes)
            if (Array.IndexOf(set, b) >= 0) return true;
        return false;
    }

    public Persistence.WorldMeta.WeatherModel ToModel() => new(Precipitation, Intensity, Current, PrecipitationTicksLeft, Windy, WindyTicksLeft,
        Sandstorm, SandstormTicksLeft, new[] { Wind.X, Wind.Y, WindTarget.X, WindTarget.Y }, LastHour);

    public void Load(Persistence.WorldMeta.WeatherModel m)
    {
        Precipitation = m.Precipitation;
        Intensity = m.Intensity;
        Current = m.Current;
        PrecipitationTicksLeft = m.PrecipitationTicksLeft;
        Windy = m.Windy;
        WindyTicksLeft = m.WindyTicksLeft;
        Sandstorm = m.Sandstorm;
        SandstormTicksLeft = m.SandstormTicksLeft;
        if (m.Wind is { Length: 4 } w)
        {
            Wind = new Vector2(w[0], w[1]);
            WindTarget = new Vector2(w[2], w[3]);
        }
        LastHour = m.LastHour;
    }
}
