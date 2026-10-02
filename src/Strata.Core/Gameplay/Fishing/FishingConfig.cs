using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Fishing;

/// <summary>
/// Fishing tuning (data/config/fishing.json): the fishing power multipliers for weather, time of day and the moon,
/// the pool-size penalty, bait consumption, crate chance, catch rarity tiers and the loot tables a catch rolls.
/// Defaults mirror the shipped file.
/// </summary>
public sealed record FishingConfig
{
    public const string Path = "data/config/fishing.json";

    /// <summary>Rain anywhere in the world (Terraria: ×1.2), and a thunderstorm on top of that (×1.1).</summary>
    public float RainMultiplier { get; init; } = 1.2f;
    public float StormMultiplier { get; init; } = 1.1f;

    /// <summary>Hours of the day that change fishing power: dawn and dusk bite best, midday worst.</summary>
    public FishingHours[] Hours { get; init; } =
    {
        new() { From = 4.5f, To = 6f, Multiplier = 1.3f },
        new() { From = 18f, To = 19.5f, Multiplier = 1.3f },
        new() { From = 9.5f, To = 15.5f, Multiplier = 0.8f },
    };

    /// <summary>Multiplier by moon phase (0 full … 4 new … 7).</summary>
    public float[] MoonMultipliers { get; init; } = { 1.1f, 1.05f, 1f, 0.95f, 0.9f, 0.95f, 1f, 1.05f };

    /// <summary>World events that change fishing power (the Blood Moon ×1.1).</summary>
    public Dictionary<string, float> EventMultipliers { get; init; } = new() { ["blood_moon"] = 1.1f };

    /// <summary>Connected liquid cells for full power; fewer scale it down, and below the minimum nothing bites.</summary>
    public int FullPoolCells { get; init; } = 300;
    public int MinPoolCells { get; init; } = 40;

    /// <summary>Bait is used up with chance 1 / (1 + baitPower / divisor + bait saving) on each catch.</summary>
    public float BaitDivisor { get; init; } = 6f;

    /// <summary>Base chance that a catch is a crate (the Crate Potion adds to it).</summary>
    public float CrateChance { get; init; } = 0.1f;

    /// <summary>
    /// Catch rarities, commonest first: each is rolled with chance 1 / max(<c>floor</c>, <c>odds</c> / power), and the
    /// rarest success picks the tier. Tier tables are named <c>&lt;table&gt;_&lt;id&gt;</c>; a tier with no table (or no
    /// eligible entry there) falls back to the next commoner one, down to the plain table.
    /// </summary>
    public FishingRarity[] Rarities { get; init; } =
    {
        new() { Id = "uncommon", Odds = 300, Floor = 3 },
        new() { Id = "rare", Odds = 1050, Floor = 4 },
        new() { Id = "very_rare", Odds = 2250, Floor = 5 },
        new() { Id = "legendary", Odds = 4500, Floor = 6 },
    };

    /// <summary>Plain catch tables by liquid, the crate tables (water and honey, lava) and the quest fish table.</summary>
    public string WaterTable { get; init; } = "fishing";
    public string HoneyTable { get; init; } = "fishing";
    public string LavaTable { get; init; } = "fishing_lava";
    public string CrateTable { get; init; } = "fishing_crate";
    public string LavaCrateTable { get; init; } = "fishing_lava_crate";
    public string QuestTable { get; init; } = "fishing_quest";

    /// <summary>Quest fish need at least this rarity tier (1 = uncommon).</summary>
    public int QuestTier { get; init; } = 1;

    /// <summary>Message shown when casting without bait.</summary>
    public string NoBaitMessage { get; init; } = "You need bait to fish.";

    public static FishingConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<FishingConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}

public sealed class FishingHours
{
    public float From { get; init; }
    public float To { get; init; }
    public float Multiplier { get; init; } = 1f;
}

public sealed class FishingRarity
{
    public string Id { get; init; } = "";
    public float Odds { get; init; } = 300;
    public float Floor { get; init; } = 2;
}
