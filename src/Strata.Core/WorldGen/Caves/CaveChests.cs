namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// Which chest stands where underground. Chest types rank wooden (and the biome-themed wooden-tier chests) below the Gold
/// Chest below the Obsidian Chest; each type has its own loot table, and every table's coins, stacks and rarer entries
/// also grow with depth (data/loot/containers.json, the <c>worldDepth</c> condition and <c>depthScale</c>).
/// </summary>
public static class CaveChests
{
    /// <summary>Normalized depth from which random cave chests are Gold Chests.</summary>
    public const float GoldDepth = 0.4f;

    /// <summary>The wooden-tier chest of a biome: the tundra's boreal, the jungle's mahogany, the desert's cactus….</summary>
    public static string WoodenFor(string biome) => biome switch
    {
        "tundra" => "boreal_chest",
        "jungle" => "rich_mahogany_chest",
        "desert" or "underground_desert" => "cactus_chest",
        "corruption" => "ebonwood_chest",
        "crimson" => "shadewood_chest",
        "mushroom" => "mushroom_chest",
        _ => "wooden_chest",
    };

    /// <summary>A planned cave-network chest and its loot table: themed wooden-tier chests in the underground rooms, Gold
    /// Chests in the caverns and every treasure niche above the abyss, Obsidian Chests in the abyss.</summary>
    public static (string Object, string Loot) ForRoom(CaveRoomFeature room, string biome)
    {
        if (room.Level >= CaveTuning.Levels - 1) return ("obsidian_chest", "obsidian_chest");
        if (room.Level >= 1 || room.Room == CaveRoomKind.Niche) return ("gold_chest", "gold_chest");
        return (WoodenFor(biome), "cave_chest");
    }
}
