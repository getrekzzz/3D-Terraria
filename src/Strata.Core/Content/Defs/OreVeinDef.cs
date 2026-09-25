namespace Strata.Core.Content.Defs;

/// <summary>
/// World-generation rule for an ore/gem/pocket material. Depth is normalized: 0 = sea level,
/// 1 = top of the underworld (so rules scale to every world size).
/// </summary>
public sealed class OreVeinDef : ContentDef
{
    public string Block { get; init; } = "";

    /// <summary>Blocks the vein may replace.</summary>
    public string[] Replace { get; init; } = { "stone", "deepstone" };

    public float DepthMin { get; init; }
    public float DepthMax { get; init; } = 1f;

    /// <summary>Expected number of veins per 32³ chunk inside the depth range.</summary>
    public float PerChunk { get; init; } = 1f;

    /// <summary>Vein radius range in cells.</summary>
    public float[] Size { get; init; } = { 1.5f, 3f };

    /// <summary>Restrict to these biomes (empty = anywhere).</summary>
    public string[] Biomes { get; init; } = Array.Empty<string>();

    /// <summary>Only one member of an alternate group generates per world (copper vs tin…).</summary>
    public string? Group { get; init; }

    /// <summary>Stretch factor: > 1 makes elongated seams rather than round blobs.</summary>
    public float Stretch { get; init; } = 1.6f;

    /// <summary>False: never placed by world generation, only by a world mutation (hardmode ores).</summary>
    public bool Generate { get; init; } = true;

    public float SizeMin => Size.Length > 0 ? Size[0] : 1.5f;
    public float SizeMax => Size.Length > 1 ? Size[1] : SizeMin;
}
