namespace Strata.Core.Content.Specs;

/// <summary>What a world mutation does (see <see cref="Defs.WorldMutationDef"/>).</summary>
public static class MutationSpecs
{
    public const string OreVeins = "oreVeins";
    public const string Wedge = "wedge";

    public static void Register(SpecCatalog c)
    {
        c.Register<OreVeinsMutationParams>(SpecKind.Mutation, OreVeins);
        c.Register<WedgeMutationParams>(SpecKind.Mutation, Wedge);
    }
}

/// <summary>
/// Seeds new ore veins through the stone of the world (ore vein rules with <c>generate: false</c>). Of the veins
/// sharing an alternate group one is chosen per world, as at generation.
/// </summary>
public sealed class OreVeinsMutationParams : SpecParams, IValidatableSpec
{
    public string[] Veins { get; init; } = Array.Empty<string>();

    public string? Validate() => Veins.Length == 0 ? "veins must list at least one ore vein" : null;
}

/// <summary>
/// Converts a slanted slab of the world through a conversion table: from a random point on the surface it leans
/// down into the ground, <see cref="HalfWidth"/> thick and <see cref="Length"/> long, and reaches
/// <see cref="Depth"/> into the world. The slab also becomes the conversion's zone (biome).
/// </summary>
public sealed class WedgeMutationParams : SpecParams, IValidatableSpec
{
    /// <summary>Conversion table id; or pick one by the world's evil kind (umbral/sanguine) with <see cref="ByEvil"/>.</summary>
    public string? Conversion { get; init; }

    public Dictionary<string, string> ByEvil { get; init; } = new();

    /// <summary>Half thickness of the slab (cells).</summary>
    public float HalfWidth { get; init; } = 14f;

    /// <summary>Length of the slab along the surface (cells).</summary>
    public float Length { get; init; } = 220f;

    /// <summary>How far the slab shifts sideways per cell of depth (slant).</summary>
    public float Lean { get; init; } = 0.8f;

    /// <summary>Normalized depth the slab reaches (0 = sea level, 1 = the underworld's top).</summary>
    public float Depth { get; init; } = 0.9f;

    /// <summary>Where the slab starts: this fraction of the land radius from the world centre.</summary>
    public float Distance { get; init; } = 0.35f;

    public string? Validate() =>
        (Conversion == null) == (ByEvil.Count == 0) ? "set exactly one of conversion or byEvil"
        : HalfWidth <= 0 || Length <= 0 ? "halfWidth and length must be > 0"
        : Depth <= 0 ? "depth must be > 0" : null;
}
