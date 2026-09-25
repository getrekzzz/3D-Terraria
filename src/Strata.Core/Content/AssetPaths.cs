using Strata.Core.Content.Defs;

namespace Strata.Core.Content;

/// <summary>
/// Where generated assets live (tools/blender/build_all.py and tools/textures/build_icons.py write them).
/// Paths are project-relative; the engine prefixes <c>res://</c>.
/// </summary>
public static class AssetPaths
{
    public static string ItemIcon(ItemDef item) => $"assets/icons/items/{item.Icon ?? item.Id}.png";

    /// <summary>Model for a tile object variant.</summary>
    public static string ObjectModel(TileObjectDef def, int variant) =>
        def.Model != null ? $"assets/models/{def.Model}_v{variant}.glb" : $"assets/models/objects/{def.Id}_v{variant}.glb";

    /// <summary>Hand-held/dropped model for an item, or null when the item is a block (drawn from terrain textures).</summary>
    public static string? ItemModel(ItemDef item, ContentDatabase content)
    {
        if (item.Model != null) return $"assets/models/{item.Model}.glb";
        if (item.PlaceBlock != null) return null;
        if (item.PlaceObject != null && content.TileObjects.TryGet(item.PlaceObject, out var obj)) return ObjectModel(obj, 0);
        return $"assets/models/items/{item.Id}.glb";
    }

    public static string CreatureModel(EnemyDef enemy) => $"assets/models/creatures/{enemy.Model ?? enemy.Id}.glb";
}
