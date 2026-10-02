using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Banners;

/// <summary>Enemy banner tuning (data/config/banners.json), as in Terraria.</summary>
public sealed class BannerConfig
{
    public const string Path = "data/config/banners.json";

    /// <summary>Kills of a banner's creatures (world-wide) per banner dropped.</summary>
    public int KillsPerBanner { get; init; } = 50;

    /// <summary>How far (cells, a cube's half size) a placed banner reaches.</summary>
    public int Radius { get; init; } = 64;

    /// <summary>Damage dealt to a banner's creatures by players near it (multiplier).</summary>
    public float DamageDealt { get; init; } = 1.5f;

    /// <summary>Contact damage taken from a banner's creatures by players near it (multiplier).</summary>
    public float DamageTaken { get; init; } = 0.9f;

    /// <summary>Ticks between scans for nearby banners.</summary>
    public int RefreshTicks { get; init; } = 30;

    /// <summary>The buff shown while any banner is near.</summary>
    public string Buff { get; init; } = "monster_banner";

    public static BannerConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<BannerConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
