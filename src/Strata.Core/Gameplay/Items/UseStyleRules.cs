using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// What an item's use style means for gameplay: the actions a style implies (a sword's swing hits, a spear thrusts,
/// a gun or a boomerang shoots, a potion is drunk) and the style an item without one in data gets from its stats.
/// </summary>
public static class UseStyleRules
{
    /// <summary>The item's style: its own, or one inferred from its stats when data leaves it out.</summary>
    public static UseStyle Resolve(ItemDef item) => item.UseStyle != UseStyle.None ? item.UseStyle : Infer(item);

    /// <summary>
    /// The style for an item without one: tools swing, placers place, food (Well Fed) is eaten and other healing or
    /// buffing items are drunk, summons and other consumed items are held up, hooks are flung out along the aim,
    /// arrow weapons are bows, other ammo weapons guns, and magic weapons with a projectile staffs. None otherwise.
    /// </summary>
    public static UseStyle Infer(ItemDef item)
    {
        if (item.IsTool || Has(item, ItemBehaviorSpecs.MineTool) || Has(item, ItemBehaviorSpecs.ChopTool) || Has(item, ItemBehaviorSpecs.HammerTool))
            return UseStyle.Swing;
        if (item.PlaceBlock != null || item.PlaceObject != null) return UseStyle.Place;
        if (item.Buff?.Id == WellFed) return UseStyle.Eat;
        if (item.HealLife > 0 || item.HealMana > 0 || item.Buff != null) return UseStyle.Drink;
        if (Has(item, ItemBehaviorSpecs.SummonBoss) || Has(item, ItemBehaviorSpecs.StartEvent) || Has(item, ItemBehaviorSpecs.Consume))
            return UseStyle.HoldUp;
        if (Has(item, ItemBehaviorSpecs.Grapple)) return UseStyle.Shoot;
        if (item.UseAmmo != null) return item.UseAmmo == "arrow" ? UseStyle.Bow : UseStyle.Gun;
        if (item.DamageType == DamageClass.Magic && item.Projectile != null) return UseStyle.Staff;
        return UseStyle.None;
    }

    /// <summary>
    /// The actions <paramref name="style"/> implies for <paramref name="item"/>. A swing hits only with a melee weapon
    /// (summon staves and fishing rods swing harmlessly) and a rapier or spear thrusts only then; noMelee items never
    /// hit. Every style that releases something shoots when the item has a projectile or ammo (a summon staff swings
    /// out its minion). Whips lash (WhipSystem). Drinking, eating and holding up use the item when it heals or buffs
    /// (consume and loot behaviours add the action themselves).
    /// </summary>
    public static ItemAction Implied(UseStyle style, ItemDef item)
    {
        bool hits = item.Damage > 0 && !item.NoMelee;
        bool melee = hits && item.DamageType == DamageClass.Melee;
        bool shoots = item.Projectile != null || item.UseAmmo != null;
        var shot = shoots ? ItemAction.Shoot : ItemAction.None;
        return style switch
        {
            UseStyle.Swing => (melee ? ItemAction.Melee : ItemAction.None) | shot,
            UseStyle.Rapier or UseStyle.Spear => (melee ? ItemAction.Thrust : ItemAction.None) | shot,
            UseStyle.Whip => hits ? ItemAction.Whip : ItemAction.None,
            UseStyle.HoldUp or UseStyle.Drink or UseStyle.Eat =>
                item.HealLife > 0 || item.HealMana > 0 || item.Buff != null ? ItemAction.Consume : ItemAction.None,
            _ when Shoots(style) => shot,
            _ => ItemAction.None,
        };
    }

    /// <summary>Styles that release something (a projectile, ammo, a thrown item): data must give them a projectile or ammo.</summary>
    public static bool Shoots(UseStyle style) => style is UseStyle.Shoot or UseStyle.Gun or UseStyle.Bow or UseStyle.Staff
        or UseStyle.Tome or UseStyle.Throw or UseStyle.Yoyo or UseStyle.Flail;

    /// <summary>The food buff: an item granting it is eaten rather than drunk.</summary>
    public const string WellFed = "well_fed";

    private static bool Has(ItemDef item, string behavior)
    {
        foreach (var spec in item.Behaviors)
            if (spec.Type == behavior) return true;
        return false;
    }
}
