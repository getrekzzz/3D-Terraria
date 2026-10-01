namespace Strata.Core.WorldGen;

/// <summary>
/// Bump whenever generator output changes for the same seed. Stored in saves; golden-hash tests pin it.
/// </summary>
public static class GenVersion
{
    public const int Current = 10; // 10: smooth relief (B-spline plan heights, soft ridges, wide biome ramps, relief from data/biomes.json); 9: cabins stand in the cave tunnels (one or two rooms), Gold Chests in the cavern layer, more mineshafts with a chest at the end; 8: the Aether (a Shimmer lake in the caverns); 7: cavern Life Crystals, Shadewood; 6: alchemy herbs and Life Fruit; 5: Jungle Temple, Plantera's Bulbs; 4: shadow orbs/crimson hearts, demon altars, bee larvae; 3: living trees can be climbed out of (staggered platforms, wider door); thinner forests
}
