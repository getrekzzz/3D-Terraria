using Strata.Core.Content.Defs;

namespace Strata.Core.Content;

/// <summary>
/// Where generated assets live (tools/blender/build_all.py, tools/textures/build_icons.py and build_ui.py write them).
/// Paths are project-relative; the engine prefixes <c>res://</c>.
/// </summary>
public static class AssetPaths
{
    public static string ItemIcon(ItemDef item) => $"assets/icons/items/{item.Icon ?? item.Id}.png";

    /// <summary>A pixel-art UI sprite (heart, star, slot silhouette, pointer…) from tools/textures/build_ui.py.</summary>
    public static string UiSprite(string name) => $"assets/ui/{name}.png";

    /// <summary>The 32 px buff or debuff icon (tools/textures/build_ui.py, recipe in buff_icons.json).</summary>
    public static string BuffIcon(BuffDef buff) => $"assets/icons/buffs/{buff.Icon ?? buff.Id}.png";

    /// <summary>The 32 px world event icon (tools/textures/build_ui.py, recipe in event_icons.json).</summary>
    public static string EventIcon(EventDef ev) => $"assets/icons/events/{ev.Icon ?? ev.Id}.png";

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

    public static string TownNpcModel(TownNpcDef npc) => $"assets/models/creatures/{npc.Model ?? npc.Id}.glb";

    /// <summary>The boss bar's head icon of a (non-minor) boss, rendered from its model (build_all.py --boss-icons).</summary>
    public static string BossIcon(EnemyDef boss) => $"assets/icons/bosses/{boss.Id}.png";

    /// <summary>A town NPC's head portrait (housing banners, the Guide book), rendered by build_all.py --portraits.</summary>
    public static string TownNpcHead(TownNpcDef npc) => $"assets/icons/npcs/{npc.Id}.png";

    /// <summary>A creature's bestiary thumbnail, rendered by build_all.py --portraits.</summary>
    public static string CreatureIcon(EnemyDef enemy) => $"assets/icons/creatures/{enemy.Id}.png";

    public static string ProjectileModel(ProjectileDef projectile) => $"assets/models/projectiles/{projectile.Model ?? projectile.Id}.glb";
}
