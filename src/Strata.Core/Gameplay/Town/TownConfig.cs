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

    /// <summary>Terraria happiness: biome and neighbour preferences, crowding, solitude (prices).</summary>
    public HappinessConfig Happiness { get; init; } = new();

    /// <summary>Biome pylons: which resident sells which pylon, and when a pylon works (P5-34).</summary>
    public PylonConfig Pylons { get; init; } = new();

    /// <summary>Emote bubbles and voice blips (X-05).</summary>
    public EmoteConfig Emotes { get; init; } = new();

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
