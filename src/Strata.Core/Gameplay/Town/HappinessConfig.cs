namespace Strata.Core.Gameplay.Town;

/// <summary>Terraria 1.4 happiness rules (data/config/town.json "happiness"). Multipliers apply to prices.</summary>
public sealed class HappinessConfig
{
    public float Love { get; init; } = 0.88f;
    public float Like { get; init; } = 0.94f;
    public float Dislike { get; init; } = 1.06f;
    public float Hate { get; init; } = 1.12f;

    /// <summary>Neighbours within this horizontal distance (m; 25 tiles) count as near.</summary>
    public float NearRadius { get; init; } = 12.5f;

    /// <summary>Residents within this horizontal distance (m; 120 tiles) count toward solitude.</summary>
    public float FarRadius { get; init; } = 60f;

    /// <summary>Residents more than this far above or below do not count (stacked houses).</summary>
    public float VerticalCap { get; init; } = 15f;

    /// <summary>More near neighbours than this makes a resident feel crowded, <see cref="CrowdStep"/> each.</summary>
    public int CrowdLimit { get; init; } = 3;
    public float CrowdStep { get; init; } = 0.05f;

    /// <summary>At most this many near and <see cref="SolitudeFar"/> far neighbours: the resident loves the solitude.</summary>
    public int SolitudeNear { get; init; } = 2;
    public int SolitudeFar { get; init; } = 4;
    public float Solitude { get; init; } = 0.95f;

    /// <summary>A resident without a home charges more.</summary>
    public float Homeless { get; init; } = 1.1f;

    /// <summary>Dialogue templates per reason ({biome}, {neighbor}); per-NPC overrides in TownNpcDef.HappinessLines.</summary>
    public Dictionary<string, string> Lines { get; init; } = new()
    {
        ["loveBiome"] = "I love living in the {biome}!",
        ["likeBiome"] = "I like living in the {biome}.",
        ["dislikeBiome"] = "I don't like living in the {biome}.",
        ["hateBiome"] = "I hate living in the {biome}!",
        ["loveNeighbor"] = "I love having {neighbor} around!",
        ["likeNeighbor"] = "I like being near {neighbor}.",
        ["dislikeNeighbor"] = "I don't like being near {neighbor}.",
        ["hateNeighbor"] = "I can't stand being near {neighbor}.",
        ["princess"] = "The Princess brightens my day.",
        ["crowded"] = "It's too crowded here.",
        ["solitude"] = "I love the peace and quiet out here.",
        ["homeless"] = "I need a place to call home.",
        ["content"] = "I'm content.",
    };
}
