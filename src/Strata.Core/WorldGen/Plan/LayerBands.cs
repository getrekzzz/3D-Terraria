using Strata.Core.World;

namespace Strata.Core.WorldGen.Plan;

/// <summary>Absolute vertical layout of a world, derived from its height (all values in cells, +Y up).</summary>
public sealed class LayerBands
{
    public LayerBands(int height)
    {
        Height = height;
        UnderworldTop = (int)(height * 0.14f);
        CavernTop = (int)(height * 0.49f);
        UndergroundTop = (int)(height * 0.625f);
        SeaLevel = (int)(height * 0.66f);
        SkyStart = (int)(height * 0.80f);
        SpaceStart = (int)(height * 0.93f);
        LavaLevel = UnderworldTop / 5;
        UnderworldCeiling = UnderworldTop - UnderworldTop / 10;
    }

    public int Height { get; }
    public int UnderworldTop { get; }

    /// <summary>Bottom of the solid rock ceiling above the underworld cavity.</summary>
    public int UnderworldCeiling { get; }
    public int LavaLevel { get; }
    public int CavernTop { get; }
    public int UndergroundTop { get; }
    public int SeaLevel { get; }
    public int SkyStart { get; }
    public int SpaceStart { get; }

    public WorldLayer LayerAt(float y)
    {
        if (y >= SpaceStart) return WorldLayer.Space;
        if (y >= SkyStart) return WorldLayer.Sky;
        if (y >= UndergroundTop) return WorldLayer.Surface;
        if (y >= CavernTop) return WorldLayer.Underground;
        if (y >= UnderworldTop) return WorldLayer.Cavern;
        return WorldLayer.Underworld;
    }

    /// <summary>0 at sea level, 1 at the underworld's top, negative above sea level.</summary>
    public float NormalizedDepth(float y) => (SeaLevel - y) / (SeaLevel - UnderworldTop);

    public float YForNormalizedDepth(float depth) => SeaLevel - depth * (SeaLevel - UnderworldTop);
}
