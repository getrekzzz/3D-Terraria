using Strata.Core.World;

namespace Strata.Core.WorldGen.Plan;

/// <summary>
/// The Don't Dig Up layout (+Y up): bedrock, the ground under the bottom surface, the surface around
/// <see cref="LayerBands.SeaLevel"/> with open air above it (its upper part the Sky layer) up to the rock roof at
/// <see cref="LayerBands.UndergroundTop"/>, the underground and cavern layers, then the Underworld above
/// <see cref="LayerBands.UnderworldTop"/>: an ash floor, the lava sea at <see cref="LayerBands.LavaLevel"/>, the cavity
/// and a thin ash crust under the world's top. Normalized depth keeps the ordinary values at every layer boundary (so
/// depth-driven content lands in the matching layer) but grows upwards above the roof; under the bottom surface it grows
/// downwards as usual, reaching about the cavern's depth at the bedrock.
/// </summary>
public sealed partial class LayerBands
{
    private float _depthUnderground, _depthCavern, _bottomSlope;

    /// <summary>Inverted worlds: the bottom of the surface layer (the ground below it is the underground).</summary>
    public int SurfaceFloor { get; private set; }

    private void InitInverted(int height)
    {
        // The ordinary layout's depths at its layer boundaries.
        int sea = (int)(height * 0.66f), underworld = (int)(height * 0.14f);
        float span = sea - underworld;
        _depthUnderground = (sea - (int)(height * 0.625f)) / span;
        _depthCavern = (sea - (int)(height * 0.49f)) / span;

        SeaLevel = (int)(height * 0.20f);
        SkyStart = (int)(height * 0.29f);
        UndergroundTop = (int)(height * 0.37f);
        CavernTop = (int)(height * 0.49f);
        UnderworldTop = (int)(height * 0.82f);
        LavaLevel = (int)(height * 0.86f);
        UnderworldCeiling = (int)(height * 0.955f);
        UnderworldSpan = underworld;
        SpaceStart = height;
        _bottomSlope = _depthCavern * 0.95f / SeaLevel;
        SurfaceFloor = (int)(SeaLevel - _depthUnderground / _bottomSlope);
    }

    private WorldLayer LayerAtInverted(float y)
    {
        if (y >= UnderworldTop) return WorldLayer.Underworld;
        if (y >= CavernTop) return WorldLayer.Cavern;
        if (y >= UndergroundTop) return WorldLayer.Underground;
        if (y >= SkyStart) return WorldLayer.Sky;
        if (y >= SurfaceFloor) return WorldLayer.Surface;
        return WorldLayer.Underground;
    }

    private float InvertedDepth(float y)
    {
        if (y < UndergroundTop) return (SeaLevel - y) * _bottomSlope;
        if (y < CavernTop) return _depthUnderground + (y - UndergroundTop) / (CavernTop - UndergroundTop) * (_depthCavern - _depthUnderground);
        return _depthCavern + (y - CavernTop) / (UnderworldTop - CavernTop) * (1f - _depthCavern);
    }

    private float InvertedY(float depth)
    {
        if (depth < _depthUnderground) return SeaLevel - depth / _bottomSlope;
        if (depth < _depthCavern) return UndergroundTop + (depth - _depthUnderground) / (_depthCavern - _depthUnderground) * (CavernTop - UndergroundTop);
        return CavernTop + (depth - _depthCavern) / (1f - _depthCavern) * (UnderworldTop - CavernTop);
    }

    private float InvertedDescentY(float depth) =>
        MathF.Max(12f, SeaLevel - SeaLevel * Math.Clamp(depth / _depthCavern, 0f, 0.9f));
}
