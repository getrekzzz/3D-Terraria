using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static void ValidateItems(Ctx ctx)
    {
        var ammoClasses = ctx.Db.Items.All.Where(i => i.AmmoClass != null).Select(i => i.AmmoClass!).ToHashSet();
        foreach (var i in ctx.Db.Items.All)
        {
            if (i.MaxStack < 1) ctx.Error(i, "maxStack must be >= 1.");
            if (i.Value < 0) ctx.Error(i, "value must be >= 0.");
            if (i.UseTime < 1 || i.UseAnimation < 1) ctx.Error(i, "useTime/useAnimation must be >= 1 tick.");
            if (i.Damage > 0 && i.DamageType == DamageClass.None) ctx.Error(i, "damage > 0 requires damageType.");
            if (i.Crit is < 0 or > 100) ctx.Error(i, "crit must be 0–100.");
            if (i.MineBrush is < 1 or > 8) ctx.Error(i, "mineBrush must be 1–8.");
            ctx.Ref(i, ctx.Db.Blocks, i.PlaceBlock, "placeBlock");
            ctx.Ref(i, ctx.Db.TileObjects, i.PlaceObject, "placeObject");
            ctx.Ref(i, ctx.Db.Projectiles, i.Projectile, "projectile");
            ctx.Ref(i, ctx.Db.Buffs, i.Buff?.Id, "buff");
            ctx.Light(i, i.HeldLight, "heldLight");
            ctx.Asset(i, AssetPaths.ItemModel(i, ctx.Db), "model");
            ctx.Asset(i, AssetPaths.ItemIcon(i), "icon");
            if (i.UseAmmo != null && !ammoClasses.Contains(i.UseAmmo)) ctx.Error(i, $"useAmmo '{i.UseAmmo}' has no ammo items.");
            if (i.UseStyle == UseStyle.Shoot && i.Projectile == null && i.UseAmmo == null)
                ctx.Error(i, "shoot items need a projectile or useAmmo.");
            if (i.EquipSlot == EquipSlot.None && (i.Category is ItemCategory.Armor or ItemCategory.Accessory))
                ctx.Error(i, "armor/accessory needs equipSlot.");
            if (i.Consumable && i.MaxStack == 1 && i.PlaceBlock == null && i.PlaceObject == null)
                ctx.Warn(i, "consumable item with maxStack 1.");
            if (i.PlaceBlock != null && i.PlaceObject != null) ctx.Error(i, "item cannot place both a block and an object.");
            ctx.Specs(i, SpecKind.ItemBehavior, i.Behaviors, "behaviors");
            ctx.Specs(i, SpecKind.Effect, i.EquipEffects, "equipEffects");
        }
    }

    private static void ValidateRecipes(Ctx ctx)
    {
        var stations = ctx.Db.TileObjects.All.SelectMany(t => t.Stations).ToHashSet(StringComparer.Ordinal);
        foreach (var g in ctx.Db.RecipeGroups.All)
        {
            if (g.Items.Length == 0) ctx.Error(g, "recipe group is empty.");
            foreach (var item in g.Items) ctx.Ref(g, ctx.Db.Items, item, "items");
        }
        foreach (var r in ctx.Db.Recipes.All)
        {
            ctx.Ref(r, ctx.Db.Items, r.Output, "output");
            if (r.Count < 1) ctx.Error(r, "count must be >= 1.");
            if (r.Inputs.Length == 0) ctx.Error(r, "recipe needs inputs.");
            foreach (var input in r.Inputs)
            {
                if ((input.Item == null) == (input.Group == null)) ctx.Error(r, "each input needs exactly one of item/group.");
                ctx.Ref(r, ctx.Db.Items, input.Item, "inputs.item");
                ctx.Ref(r, ctx.Db.RecipeGroups, input.Group, "inputs.group");
                if (input.Count < 1) ctx.Error(r, "input count must be >= 1.");
                if (input.Item == r.Output) ctx.Error(r, "recipe consumes its own output.");
            }
            foreach (var s in r.Stations)
                if (!stations.Contains(s)) ctx.Error(r, $"station '{s}' is not provided by any tile object.");
            ctx.Specs(r, SpecKind.Condition, r.Conditions, "conditions");
        }
    }
}
