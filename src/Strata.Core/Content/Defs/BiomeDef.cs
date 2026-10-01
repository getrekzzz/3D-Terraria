namespace Strata.Core.Content.Defs;

public enum BiomeKind
{
    /// <summary>Large surface biome that also extends underground (forest, tundra, jungle…).</summary>
    Surface,

    /// <summary>Exists only below the surface (mushroom caverns, marble, granite…).</summary>
    Underground,

    /// <summary>Small structure-bound zone (dungeon, hive, spider nest, temple).</summary>
    Zone,

    /// <summary>A vertical layer (sky, underworld, ocean).</summary>
    Layer,
}

/// <summary>A biome's identity: visuals, audio and gameplay hooks. Placement is decided by world generation.</summary>
public sealed class BiomeDef : ContentDef
{
    public BiomeKind Kind { get; init; } = BiomeKind.Surface;

    /// <summary>Higher priority wins when several biomes apply to a position (for spawns/music).</summary>
    public int Priority { get; init; }

    /// <summary>Minimap colour #RRGGBB.</summary>
    public string MapColor { get; init; } = "#40a040";

    public string FogColor { get; init; } = "#9fb5c8";
    public float FogDensity { get; init; } = 1f;
    public string SkyTint { get; init; } = "#ffffff";

    /// <summary>Ambient light tint applied underground in this biome.</summary>
    public string AmbientTint { get; init; } = "#ffffff";

    /// <summary>Open-air daytime exposure multiplier; bright sand and snow &lt; 1 (0.4–1.5, see Presentation.ExposureModel).</summary>
    public float ExposureBias { get; init; } = 1f;

    public string? Music { get; init; }
    public string? Ambience { get; init; }

    /// <summary>Hostile/transformative biome that spreads over time.</summary>
    public bool Spreads { get; init; }

    /// <summary>Generated structure cells in this zone cannot be broken until this progression flag is set (the crypt).</summary>
    public string? LockedUntil { get; init; }

    /// <summary>Inside this zone only spawn rules that name it (biome condition) may spawn: the crypt keeps its own roster.</summary>
    public bool ExclusiveSpawns { get; init; }
}
