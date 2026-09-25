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
        new() { Role = "door", Where = RequirementPlace.Boundary, Message = "needs a door" },
        new() { Role = "light", Where = RequirementPlace.Any, Message = "needs a light source" },
        new() { Role = "table", Message = "needs a table or other flat surface" },
        new() { Role = "chair", Message = "needs a chair" },
    };

    public static HousingConfig Load(IDataSource data) =>
        JsonSerializer.Deserialize<HousingConfig>(data.ReadText(Path), ContentJson.Options) ?? throw new JsonException($"{Path} is empty");
}
