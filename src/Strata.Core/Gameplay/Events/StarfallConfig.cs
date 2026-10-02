using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Events;

/// <summary>Fallen Star tuning (data/config/starfall.json, P3-30).</summary>
public sealed class StarfallConfig
{
    public const string Path = "data/config/starfall.json";

    /// <summary>Chance per second, per player on the surface at night, that a star falls near them.</summary>
    public float ChancePerSecond { get; init; } = 0.022f;

    /// <summary>How far from the player (metres, horizontally) a star may land.</summary>
    public float Radius { get; init; } = 40f;

    /// <summary>How high above the player (metres) a star starts; lowered to the loaded sky when that is shorter.</summary>
    public float Height { get; init; } = 45f;

    /// <summary>The lowest start (metres above the player) worth a streak; a lower sky means no star.</summary>
    public float MinHeight { get; init; } = 12f;

    /// <summary>Fall speed in m/s.</summary>
    public float Speed { get; init; } = 18f;

    /// <summary>Most stars in the air at once.</summary>
    public int MaxActive { get; init; } = 4;

    /// <summary>The projectile that falls (it leaves the Fallen Star item where it lands).</summary>
    public string Projectile { get; init; } = "falling_star";

    public IEnumerable<string> Validate()
    {
        if (ChancePerSecond is < 0 or > 1) yield return "chancePerSecond must be within 0–1";
        if (Radius < 0) yield return "radius must be >= 0";
        if (MinHeight <= 0 || Height < MinHeight) yield return "height must be >= minHeight > 0";
        if (Speed <= 0) yield return "speed must be > 0";
        if (MaxActive < 0) yield return "maxActive must be >= 0";
        if (string.IsNullOrEmpty(Projectile)) yield return "projectile is required";
    }

    public static StarfallConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<StarfallConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
