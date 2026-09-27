namespace Strata.Core.Content.Defs;

/// <summary>
/// A change to the whole world that happens once, when a progression flag is raised (hardmode's new ores and its
/// radiant and blighted wedges). Mutations are applied lazily: loaded chunks at once, every other chunk when it is
/// next generated or loaded, so the result is identical however late a region is first visited. Appended to the
/// world's mutation log in trigger order; never reorder or remove a shipped mutation.
/// </summary>
public sealed class WorldMutationDef : ContentDef
{
    /// <summary>Progression flag whose raising appends this mutation.</summary>
    public string Trigger { get; init; } = "";

    /// <summary>What it does (see <see cref="Specs.MutationSpecs"/>), e.g. { "type": "oreVeins", ... }.</summary>
    public TypedSpec Step { get; init; } = TypedSpec.Of("none");
}

/// <summary>A block conversion table (the radiant reach, the blights): what each block becomes, and the zone it forms.</summary>
public sealed class ConversionDef : ContentDef
{
    /// <summary>Biome the converted region counts as (spawns, music).</summary>
    public string? Zone { get; init; }

    /// <summary>From block id to block id.</summary>
    public Dictionary<string, string> Map { get; init; } = new();
}

/// <summary>
/// A spreading biome: while its flag is set, random cells of the source blocks convert neighbouring blocks through a
/// conversion table (radiant or blighted turf creeping over grass and stone).
/// </summary>
public sealed class SpreadRuleDef : ContentDef
{
    public string Conversion { get; init; } = "";

    /// <summary>Blocks that spread (default: every target of the conversion).</summary>
    public string[] Sources { get; init; } = Array.Empty<string>();

    /// <summary>Only spreads while this flag is set (null = always).</summary>
    public string? RequiresFlag { get; init; }

    /// <summary>Chance that a random update of a source cell converts one neighbour.</summary>
    public float Chance { get; init; } = 0.25f;

    /// <summary>Neighbour search radius in cells.</summary>
    public int Radius { get; init; } = 1;
}
