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

    /// <summary>
    /// Surface shape where world generation places this biome as a surface region (forest's also shapes the land
    /// outside every region). World generation reads it: editing a value changes worlds and needs a GenVersion bump.
    /// </summary>
    public ReliefSpec? Relief { get; init; }
}

/// <summary>
/// A surface biome's relief in cells above the coast: a base rise plus soft noise terms (each term's amplitude is its
/// largest contribution), blended across region borders by world generation.
/// </summary>
public sealed class ReliefSpec
{
    /// <summary>Rise above the shore level everywhere in the biome.</summary>
    public float Base { get; init; }

    /// <summary>Rolling hills: the product of two crossed hill fields, so broad swells with calm hollows between.</summary>
    public float Rolling { get; init; }

    /// <summary>Plain broad hills (λ ≈ 190 cells).</summary>
    public float Hills { get; init; }

    /// <summary>Long soft-crested ridges (snow ridges, λ ≈ 240 cells).</summary>
    public float Ridges { get; init; }

    /// <summary>Smaller mounds on top (λ ≈ 90 cells).</summary>
    public float Bumps { get; init; }

    /// <summary>Soft-crested dunes (λ ≈ 70 cells).</summary>
    public float Dunes { get; init; }

    /// <summary>0–1: amplitude of the 3D overhang noise near the surface.</summary>
    public float Ruggedness { get; init; }

    /// <summary>0–1: affinity for steep cliff zones.</summary>
    public float Cliffs { get; init; }

    /// <summary>Mountain ranges raised under the large-scale mountain mask; only the forest's (the open land's) are used.</summary>
    public MountainSpec? Mountains { get; init; }
}

/// <summary>Mountain ranges: a plateau of <see cref="Height"/> cells under the mask plus soft-crested peaks up to <see cref="Peak"/> more.</summary>
public sealed class MountainSpec
{
    public float Height { get; init; }
    public float Peak { get; init; }
}
