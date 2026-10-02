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
            if (i.ReuseDelay < 0) ctx.Error(i, "reuseDelay must be >= 0 ticks.");
            if (i.AmmoPerBurst && (i.UseAmmo == null || i.UseTime >= i.UseAnimation))
                ctx.Error(i, "ammoPerBurst needs useAmmo and a burst (useTime < useAnimation).");
            if (i.Damage > 0 && i.DamageType == DamageClass.None) ctx.Error(i, "damage > 0 requires damageType.");
            if (i.Crit is < 0 or > 100) ctx.Error(i, "crit must be 0–100.");
            if (i.AmmoSaveChance is < 0f or > 1f || (i.AmmoSaveChance > 0f && i.UseAmmo == null)) ctx.Error(i, "ammoSaveChance must be within 0–1 and needs useAmmo.");
            if (i.MineBrush is < 1 or > 8) ctx.Error(i, "mineBrush must be 1–8.");
            if (i.Rarity is < -13 or > 13) ctx.Error(i, "rarity must be -13–13 (Terraria's rarity tiers).");
            if (i.Buff != null && ctx.Db.Buffs.TryGet(i.Buff.Id, out var granted) && !Describable(granted, ctx.Db))
                ctx.Warn(i, $"buff '{granted.Id}' has no description and no effects a tooltip can describe.");
            ctx.Ref(i, ctx.Db.Blocks, i.PlaceBlock, "placeBlock");
            ctx.Ref(i, ctx.Db.TileObjects, i.PlaceObject, "placeObject");
            ctx.Ref(i, ctx.Db.Projectiles, i.Projectile, "projectile");
            ctx.Ref(i, ctx.Db.Buffs, i.Buff?.Id, "buff");
            ctx.Light(i, i.HeldLight, "heldLight");
            ctx.Asset(i, AssetPaths.ItemModel(i, ctx.Db), "model");
            foreach (var b in i.Behaviors.Where(b => b.Type == Specs.ItemBehaviorSpecs.SummonBoss))
            {
                if (b.Raw.TryGetProperty("enemy", out var enemy)) ctx.Ref(i, ctx.Db.Enemies, enemy.GetString(), "summonBoss.enemy");
                if (b.Raw.TryGetProperty("conditions", out var conds) && conds.ValueKind == System.Text.Json.JsonValueKind.Array)
                    ctx.Specs(i, Specs.SpecKind.Condition, conds.EnumerateArray().Select(c => new TypedSpec(c.GetProperty("type").GetString()!, c.Clone())), "summonBoss.conditions");
            }
            foreach (var b in i.Behaviors.Where(b => b.Type == Specs.ItemBehaviorSpecs.StartEvent))
                if (ctx.TryParse<Specs.StartEventParams>(SpecKind.ItemBehavior, b) is { } start)
                {
                    ctx.Ref(i, ctx.Db.Events, start.Event, "startEvent.event");
                    ctx.Specs(i, Specs.SpecKind.Condition, start.Conditions, "startEvent.conditions");
                }
            foreach (var b in i.Behaviors.Where(b => b.Type == Specs.ItemBehaviorSpecs.IncinerationSummon))
                if (ctx.TryParse<Specs.IncinerationSummonParams>(SpecKind.ItemBehavior, b) is { } burn)
                {
                    ctx.Ref(i, ctx.Db.Enemies, burn.Enemy, "incinerationSummon.enemy");
                    if (ctx.Db.Enemies.TryGet(burn.Enemy, out var boss) && boss.Boss == null) ctx.Error(i, $"incinerationSummon.enemy '{burn.Enemy}' is not a boss.");
                    ctx.Ref(i, ctx.Db.TownNpcs, burn.KillsResident, "incinerationSummon.killsResident");
                }
            foreach (var b in i.Behaviors)
            {
                if (b.Type == Specs.ItemBehaviorSpecs.Wiring && ctx.TryParse<Specs.WiringParams>(SpecKind.ItemBehavior, b) is { } wiring)
                    ctx.Ref(i, ctx.Db.Items, wiring.Wire, "wiring.wire");
                if (b.Type == Specs.ItemBehaviorSpecs.ReleaseCritter && ctx.TryParse<Specs.ReleaseCritterParams>(SpecKind.ItemBehavior, b) is { } release)
                    ctx.Ref(i, ctx.Db.Enemies, release.Enemy, "releaseCritter.enemy");
                if (i.NoMelee && b.Type is Specs.ItemBehaviorSpecs.MeleeSwing or Specs.ItemBehaviorSpecs.Thrust)
                    ctx.Error(i, $"noMelee items cannot have a {b.Type} behaviour (their swing is animation only).");
                if (b.Type == Specs.ItemBehaviorSpecs.ShootProjectile && ctx.TryParse<Specs.ShootProjectileParams>(SpecKind.ItemBehavior, b) is { } shoot
                    && i.Projectile != null && ctx.Db.Projectiles.TryGet(i.Projectile, out var shot) && shoot.Limit(shot.Returns) is > 0 and var limit && shoot.Count > limit)
                    ctx.Error(i, $"shootProjectile fires {shoot.Count} '{shot.Id}' per use but only {limit} may be out at once (set maxActive).");
            }
            ctx.Asset(i, AssetPaths.ItemIcon(i), "icon");
            if (i.UseAmmo != null && !ammoClasses.Contains(i.UseAmmo)) ctx.Error(i, $"useAmmo '{i.UseAmmo}' has no ammo items.");
            UseStyleChecks(ctx, i);
            if (i.MaxStack > 1 && i.MaxStack != ItemDef.CommonMaxStack && i.Category != ItemCategory.Coin)
                ctx.Error(i, $"stackable items stack to {ItemDef.CommonMaxStack} (Terraria 1.4.4); only coins stop sooner.");
            if (i.EquipSlot == EquipSlot.None && (i.Category is ItemCategory.Armor or ItemCategory.Accessory))
                ctx.Error(i, "armor/accessory needs equipSlot.");
            if (MiscEquipSlot(i.Category) is { } misc && i.EquipSlot != misc && !(misc == EquipSlot.Pet && i.EquipSlot == EquipSlot.LightPet))
                ctx.Error(i, $"{i.Category} items need equipSlot '{System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(misc.ToString())}'"
                    + (misc == EquipSlot.Pet ? " or 'lightPet'." : "."));
            if (i.Consumable && i.MaxStack == 1 && i.PlaceBlock == null && i.PlaceObject == null)
                ctx.Warn(i, "consumable item with maxStack 1.");
            if (i.PlaceBlock != null && i.PlaceObject != null) ctx.Error(i, "item cannot place both a block and an object.");
            ctx.Specs(i, SpecKind.ItemBehavior, i.Behaviors, "behaviors");
            ctx.Specs(i, SpecKind.Effect, i.EquipEffects, "equipEffects");
            ctx.Specs(i, SpecKind.Effect, i.SetEffects, "setEffects");
            if ((i.SetEffects.Length > 0 || i.SetBonus != null) && i.ArmorSet == null) ctx.Error(i, "setBonus/setEffects need an armorSet.");
        }
    }

    /// <summary>
    /// Use styles: a style that releases something (gun, bow, staff, boomerang, yoyo, flail…) needs a projectile or
    /// ammo; yoyos, flails and whips need damage; a weapon, tool or consumable should not end up without a style.
    /// </summary>
    private static void UseStyleChecks(Ctx ctx, ItemDef i)
    {
        string style = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(i.UseStyle.ToString());
        if (Gameplay.Items.UseStyleRules.Shoots(i.UseStyle) && i.Projectile == null && i.UseAmmo == null)
            ctx.Error(i, $"{style} items need a projectile or useAmmo.");
        if (i.UseStyle is (UseStyle.Yoyo or UseStyle.Flail or UseStyle.Whip) && i.Damage <= 0)
            ctx.Error(i, $"{style} items need damage.");
        bool usable = (i.IsWeapon && i.AmmoClass == null) || i.IsTool || i.Category is ItemCategory.Consumable or ItemCategory.Potion;
        if (usable && Gameplay.Items.UseStyleRules.Resolve(i) == UseStyle.None)
            ctx.Warn(i, "no useStyle, and none can be inferred from its stats (it plays no use action).");
    }

    /// <summary>The misc slot items of a category must be worn in (hooks, mounts, minecarts, pets); null for other categories.</summary>
    private static EquipSlot? MiscEquipSlot(ItemCategory category) => category switch
    {
        ItemCategory.Hook => EquipSlot.Hook,
        ItemCategory.Mount => EquipSlot.Mount,
        ItemCategory.Minecart => EquipSlot.Minecart,
        ItemCategory.Pet => EquipSlot.Pet,
        _ => null,
    };

    /// <summary>Whether a tooltip can say what the buff does (malformed effects are reported by the buff checks).</summary>
    private static bool Describable(BuffDef buff, ContentDatabase db)
    {
        try
        {
            return Gameplay.Buffs.BuffText.Describe(buff, db) != null;
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
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
