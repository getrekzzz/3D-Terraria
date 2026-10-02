using System.Text.Json;
using Strata.Core.Content;
using Strata.Core.Content.Loading;

namespace Strata.Core.Gameplay.Housing;

public enum RequirementPlace
{
    /// <summary>An object occupying a room cell.</summary>
    Inside,

    /// <summary>An object in the room's walls (doors).</summary>
    Boundary,

    /// <summary>Either.</summary>
    Any,
}

/// <summary>A furniture role a home must contain, e.g. a light source or a chair.</summary>
public sealed class HousingRequirement
{
    public string Role { get; init; } = "";
    public RequirementPlace Where { get; init; } = RequirementPlace.Inside;
    public string Message { get; init; } = "";
}

/// <summary>Housing rules (data/config/housing.json).</summary>
public sealed class HousingConfig
{
    public const string Path = "data/config/housing.json";

    /// <summary>Cells a body can stand on inside the room.</summary>
    public int MinFloorCells { get; init; } = 24;

    /// <summary>Tallest column of room air must reach this many cells.</summary>
    public int MinHeadroom { get; init; } = 5;

    /// <summary>More connected air than this means the room is open (or a cave).</summary>
    public int MaxVolume { get; init; } = 3000;

    /// <summary>Share of the walls that must be built by players (natural caves are not homes).</summary>
    public float MinBuiltBoundary { get; init; } = 0.5f;

    public HousingRequirement[] Requirements { get; init; } =
    {
        new() { Role = "door", Where = RequirementPlace.Boundary, Message = "This housing is missing a door." },
        new() { Role = "light", Where = RequirementPlace.Any, Message = "This housing is missing a light source." },
        new() { Role = "table", Message = "This housing is missing a table." },
        new() { Role = "chair", Message = "This housing is missing a chair." },
    };

    /// <summary>Blocks with this tag near a room corrupt it (Corruption, Crimson)…</summary>
    public string EvilTag { get; init; } = "evil";

    /// <summary>…unless they also carry this tag (demonite and crimtane ore do not corrupt a house).</summary>
    public string EvilIgnoreTag { get; init; } = "ore";

    /// <summary>Cells around the room's bounds searched for evil blocks.</summary>
    public int EvilMargin { get; init; } = 6;

    /// <summary>Evil cells found (sampling every other column) that make the house corrupted.</summary>
    public int EvilLimit { get; init; } = 40;

    /// <summary>New room flood fills allowed per tick for the town's background checks (the rest wait).</summary>
    public int MaxScansPerTick { get; init; } = 2;

    /// <summary>Open (unenclosed) results are re-checked after this many ticks (a chunk may have loaded since).</summary>
    public int OpenRoomTtl { get; init; } = 1800;

    /// <summary>Terraria's housing query messages by key; missing keys fall back to <see cref="DefaultMessages"/>.</summary>
    public Dictionary<string, string> Messages { get; init; } = new(DefaultMessages);

    public static readonly IReadOnlyDictionary<string, string> DefaultMessages = new Dictionary<string, string>
    {
        ["suitable"] = "This housing is suitable.",
        ["notOpen"] = "This is not valid housing.",
        ["notEnclosed"] = "This is not valid housing.",
        ["tooLarge"] = "This is not valid housing.",
        ["tooSmall"] = "This room is too small.",
        ["lowCeiling"] = "This room is too small.",
        ["natural"] = "This is not valid housing.",
        ["corrupted"] = "This housing is corrupted.",
        ["noStand"] = "This room is too small.",
    };

    public string Message(string key) =>
        Messages.TryGetValue(key, out var m) ? m : DefaultMessages.TryGetValue(key, out m) ? m : key;

    public static HousingConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<HousingConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
