namespace Strata.Core.WorldGen.Passes;

/// <summary>The canonical ordered world-generation pipeline (see docs/architecture/worldgen.md).</summary>
public static class PassRegistry
{
    public static IReadOnlyList<GenPass> CreateAll() => new GenPass[]
    {
        new GlobalDensityPass(),       // 1  terrain density field
        new ContinentsPass(),          // 2  continents / coastline / base elevation
        new GeologicalLayersPass(),    // 3  strata by depth
        new BiomeMasksPass(),          // 4  biome regions, relief, biome materials
        new CaveGenerationPass(),      // 5  tunnels, worms, shafts, sinkholes, cracks
        new LargeCavernsPass(),        // 6  caverns, grand caverns, lakes, geodes, mushroom hollows
        new UndergroundRiversPass(),   // 7  rivers and lava tubes
        new OreDistributionPass(),     // 8  data-driven ore/gem/pocket veins
        new StructurePlacementPass(),  // 9  cabins, mineshafts, living trees, pyramids, shrines, mini-biomes
        new DungeonPass(),             // 10 the crypt
        new JunglePass(),              // 11 hives, temple site
        new HostileBiomePass(),        // 12 evil chasms and conversion
        new UnderworldPass(),          // 13 cinderdeep cavity, lava sea, ruins
        new FloatingIslandsPass(),     // 14 sky islands + sky houses
        new OceanPass(),               // 15 seafloor + water
        new TreasurePass(),            // 16 chests, pots, vital crystals
        new EcologyZonesPass(),        // 17 spider nests and zone registration
        new DecorationPass(),          // 18 grass, moss, trees, plants, cave decor
        new NavigationPass(),          // 19 connectivity + walkability summary
        new SimulationInitPass(),      // 20 liquid/material consistency
    };
}
