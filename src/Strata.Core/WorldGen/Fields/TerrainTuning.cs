namespace Strata.Core.WorldGen.Fields;

/// <summary>
/// Surface-shape algorithm constants (the generator's, not content: per-biome relief and cliff affinity live in
/// data/biomes.json). Changing any of these changes world output, so it needs a <see cref="GenVersion"/> bump.
/// </summary>
public static class TerrainTuning
{
    /// <summary>The near-surface 3D overhang noise: two broad octaves, stretched vertically (y × 0.8) so it varies slower in height than across.</summary>
    public const float OverhangWavelength = 48f;
    public const int OverhangOctaves = 2;
    public const float OverhangYStretch = 0.8f;

    /// <summary>
    /// Overhang amplitude (cells) = base + ruggedness × <see cref="RuggedAmp"/> + cliff × <see cref="CliffAmp"/>. Outside
    /// cliff zones it stays at most about 2 cells, so the surface is a heightfield with sub-cell jitter; inside a cliff
    /// zone it reaches about 12 cells: steep walls with occasional undercuts.
    /// </summary>
    public const float BaseAmp = 0.6f;
    public const float RuggedAmp = 1.4f;
    public const float CliffAmp = 11f;

    /// <summary>
    /// Inside cliff zones the overhang noise is terraced: steepened × <see cref="TerraceSharpness"/> and clipped to ±1,
    /// blended in as the cliff strength rises from <see cref="TerraceLow"/> to <see cref="TerraceHigh"/>. Flat shelves
    /// with steep walls between them; at the walls the vertical gradient can beat the height term, so ~1% of cliff-zone
    /// columns get an undercut. Below <see cref="TerraceLow"/> (all ordinary terrain) the noise is untouched.
    /// </summary>
    public const float TerraceSharpness = 2.5f;
    public const float TerraceLow = 0.1f;
    public const float TerraceHigh = 0.5f;

    /// <summary>Cliff zones: where the large-scale cliff mask (0–1) rises from <see cref="CliffMaskLow"/> to <see cref="CliffMaskHigh"/>.</summary>
    public const float CliffMaskWavelength = 520f;
    public const float CliffMaskLow = 0.60f;
    public const float CliffMaskHigh = 0.78f;

    /// <summary>Cliff affinity inside mountain ranges (the region's own affinity is blended towards this by the mountain mask).</summary>
    public const float MountainCliffs = 0.8f;

    /// <summary>No cliff zones within this distance (cells) of spawn; they ramp in fully by <see cref="CliffCalmOuter"/>.</summary>
    public const float CliffCalmInner = 180f;
    public const float CliffCalmOuter = 280f;

    /// <summary>
    /// Share of worm tunnels that open at the surface as cave mouths: they start one cell under the ground, head into
    /// rising ground and dive steeply (<see cref="MouthPitchMin"/>..<see cref="MouthPitchMax"/> turns, about 25–36°
    /// down) for <see cref="MouthSegments"/> segments, so the entrance is a hole in a hillside instead of an open
    /// trench. The rest start buried <see cref="BuriedWormDepthMin"/>..<see cref="BuriedWormDepthMax"/> cells down.
    /// </summary>
    public const float WormMouthFraction = 0.3f;
    public const float MouthPitchMin = -0.10f;
    public const float MouthPitchMax = -0.07f;
    public const int MouthSegments = 3;
    public const float BuriedWormDepthMin = 14f;
    public const float BuriedWormDepthMax = 40f;
}
