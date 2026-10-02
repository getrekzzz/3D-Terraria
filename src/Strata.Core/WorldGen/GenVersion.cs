namespace Strata.Core.WorldGen;

/// <summary>
/// Bump whenever generator output changes for the same seed. Stored in saves; golden-hash tests pin it.
/// </summary>
public static class GenVersion
{
    /// <summary>The first version whose Don't Dig Up worlds generate upside down (older ones keep the ordinary layout).</summary>
    public const int UpsideDownSince = 14;

    public const int Current = 14; // 14: Don't Dig Up worlds generate upside down (the underworld on top, the surface at the bottom under a rock roof; ordinary seeds unchanged); 13: rare Strange Plants on grass tops (surface turf and the underground jungle; nothing else changes); 12: secret seeds (Drunk World both evils and altars, Not the Bees jungle and hives, No Traps dungeon traps; ordinary seeds unchanged); 11: the 1:1 Dungeon (winding halls, keep, wall panels, locked chests, vault); 10: Phase 1 smooth relief (B-spline plan heights, soft ridges, wide biome ramps, relief from data/biomes.json), a heightfield surface with overhangs only in rare cliff zones, cave roofs under the true surface, worm tunnels as cave mouths or buried; 9: cabins stand in the cave tunnels (one or two rooms), Gold Chests in the cavern layer, more mineshafts with a chest at the end; 8: the Aether (a Shimmer lake in the caverns); 7: cavern Life Crystals, Shadewood; 6: alchemy herbs and Life Fruit; 5: Jungle Temple, Plantera's Bulbs; 4: shadow orbs/crimson hearts, demon altars, bee larvae; 3: living trees can be climbed out of (staggered platforms, wider door); thinner forests
}
