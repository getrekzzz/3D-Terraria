using Strata.Core.World;

namespace Strata.Core.WorldGen.Plan;

/// <summary>
/// Absolute vertical layout of a world, derived from its height (all values in cells, +Y up). An
/// <see cref="Inverted"/> layout is the Don't Dig Up world (see <see cref="LayerBands(int, bool)"/>).
/// </summary>
public sealed partial class LayerBands
{
    public LayerBands(int height) : this(height, false) { }

    /// <summary>
    /// <paramref name="inverted"/>: the Don't Dig Up layout — the Underworld near the top under a thin ash crust, the
    /// cavern and underground layers below it, and at the bottom the surface (grass, trees, the sea) under an open air
    /// pocket roofed by the underground's rock (see docs/architecture/worldgen.md, "Don't Dig Up").
    /// </summary>
    public LayerBands(int height, bool inverted)
    {
        Height = height;
        Inverted = inverted;
        if (inverted)
        {
            InitInverted(height);
            return;
        }
        UnderworldTop = (int)(height * 0.14f);
        CavernTop = (int)(height * 0.49f);
        UndergroundTop = (int)(height * 0.625f);
        SeaLevel = (int)(height * 0.66f);
        SkyStart = (int)(height * 0.80f);
        SpaceStart = (int)(height * 0.93f);
        LavaLevel = UnderworldTop / 5;
        UnderworldCeiling = UnderworldTop - UnderworldTop / 10;
        UnderworldSpan = UnderworldTop;
    }

    public int Height { get; }

    /// <summary>Don't Dig Up: depth (and every layer) grows upwards; the surface is at the bottom.</summary>
    public bool Inverted { get; }

    /// <summary>The boundary between the cavern and the underworld (inverted: the underworld lies above it).</summary>
    public int UnderworldTop { get; private set; }

    /// <summary>Bottom of the solid rock ceiling above the underworld cavity.</summary>
    public int UnderworldCeiling { get; private set; }
    public int LavaLevel { get; private set; }

    /// <summary>Thickness scale of the underworld band (cells) the cavity's floor relief is measured against.</summary>
    public int UnderworldSpan { get; private set; }
    public int CavernTop { get; private set; }

    /// <summary>Top of the underground layer (inverted: its bottom, the base of the rock roof over the surface).</summary>
    public int UndergroundTop { get; private set; }
    public int SeaLevel { get; private set; }
    public int SkyStart { get; private set; }

    /// <summary>Start of space (inverted worlds have none: <see cref="Height"/>).</summary>
    public int SpaceStart { get; private set; }

    public WorldLayer LayerAt(float y)
    {
        if (Inverted) return LayerAtInverted(y);
        if (y >= SpaceStart) return WorldLayer.Space;
        if (y >= SkyStart) return WorldLayer.Sky;
        if (y >= UndergroundTop) return WorldLayer.Surface;
        if (y >= CavernTop) return WorldLayer.Underground;
        if (y >= UnderworldTop) return WorldLayer.Cavern;
        return WorldLayer.Underworld;
    }

    /// <summary>0 at sea level, 1 at the underworld's top, negative above sea level.</summary>
    public float NormalizedDepth(float y) => Inverted ? InvertedDepth(y) : (SeaLevel - y) / (SeaLevel - UnderworldTop);

    /// <summary>A height at a normalized depth (inverted worlds: in the rock above the surface where the depth exists
    /// there, otherwise under the bottom surface).</summary>
    public float YForNormalizedDepth(float depth) => Inverted ? InvertedY(depth) : SeaLevel - depth * (SeaLevel - UnderworldTop);

    /// <summary>
    /// The height a feature dug down from the surface (a worm tunnel, an evil chasm, a hardmode wedge) bottoms out at
    /// for a normalized depth: <see cref="YForNormalizedDepth"/>, except that an inverted world's surface is at the
    /// bottom, so its descents end in the ground under it (never in the bedrock).
    /// </summary>
    public float DescentY(float depth) => Inverted ? InvertedDescentY(depth) : YForNormalizedDepth(depth);

    /// <summary>Whether <paramref name="y"/> is deeper (further from the surface) than the level <paramref name="line"/>.</summary>
    public bool IsDeeper(float y, float line) => Inverted ? y > line : y < line;

    /// <summary>The level <paramref name="cells"/> deeper than <paramref name="line"/>.</summary>
    public float Deeper(float line, float cells) => Inverted ? line + cells : line - cells;

    /// <summary>Inclusive cell range (bottom, top) of a cave layer (for cave finders).</summary>
    public (int Min, int Max) CaveRange(WorldLayer layer) => (layer, Inverted) switch
    {
        (WorldLayer.Underground, false) => (CavernTop, UndergroundTop - 1),
        (WorldLayer.Cavern, false) => (UnderworldTop, CavernTop - 1),
        (WorldLayer.Underground, true) => (UndergroundTop, CavernTop - 1),
        (WorldLayer.Cavern, true) => (CavernTop, UnderworldTop - 1),
        (WorldLayer.Underworld, _) => (LavaLevel + 2, UnderworldCeiling - 1),
        _ => throw new ArgumentException($"no caves in layer {layer}", nameof(layer)),
    };
}
