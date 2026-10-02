using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Town;

/// <summary>Town tuning (data/config/town.json).</summary>
public sealed class TownConfig
{
    public const string Path = "data/config/town.json";

    /// <summary>Ticks between move-in checks.</summary>
    public int ArrivalInterval { get; init; } = 600;

    /// <summary>Ticks between re-validating residents' homes.</summary>
    public int HomeCheckInterval { get; init; } = 900;

    /// <summary>Ticks before a fallen resident returns (only when <see cref="RespawnAtDawn"/> is off).</summary>
    public int RespawnTicks { get; init; } = 21600;

    /// <summary>Fallen residents return at the first dawn after their death (Terraria), not after a fixed delay.</summary>
    public bool RespawnAtDawn { get; init; } = true;

    /// <summary>A resident stays dead at least this long even if dawn comes sooner.</summary>
    public int MinDeadTicks { get; init; } = 1800;

    /// <summary>Residents within this distance of a home are its neighbours (happiness, crowding).</summary>
    public float NeighborRadius { get; init; } = 25f;

    /// <summary>More neighbours than this makes a resident feel crowded.</summary>
    public int CrowdLimit { get; init; } = 3;

    /// <summary>Price change per mood point (liked biome or neighbour lowers prices).</summary>
    public float MoodStep { get; init; } = 0.06f;

    /// <summary>Price increase per neighbour over the crowd limit.</summary>
    public float CrowdStep { get; init; } = 0.05f;

    public float MinPriceFactor { get; init; } = 0.75f;
    public float MaxPriceFactor { get; init; } = 1.5f;

    /// <summary>Share of an item's value paid when selling it to a resident.</summary>
    public float SellRatio { get; init; } = 0.2f;

    /// <summary>How far from home residents stroll by day.</summary>
    public float WanderRadius { get; init; } = 8f;

    /// <summary>How far residents who live at a landmark (the crypt keeper) stray from it.</summary>
    public float LandmarkRadius { get; init; } = 3f;

    public int IdleTicksMin { get; init; } = 180;
    public int IdleTicksMax { get; init; } = 540;

    /// <summary>Players must be this close to talk to a resident.</summary>
    public float TalkRange { get; init; } = 5f;

    /// <summary>A resident waits this long for the player after a conversation starts.</summary>
    public int TalkTicks { get; init; } = 900;

    public static TownConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<TownConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
