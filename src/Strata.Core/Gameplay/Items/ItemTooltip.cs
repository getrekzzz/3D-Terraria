using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Stats;

namespace Strata.Core.Gameplay.Items;

/// <summary>How a tooltip line is coloured (the UI maps each kind to its colour).</summary>
public enum TooltipKind
{
    /// <summary>The item's name, in its rarity colour.</summary>
    Name,
    Stat,
    Info,
    Flavor,

    /// <summary>A prefix modifier that helps (green).</summary>
    Good,

    /// <summary>A prefix modifier that hurts (red).</summary>
    Bad,

    /// <summary>A price whose largest coin is copper, silver, gold or platinum (coloured like that coin).</summary>
    PriceCopper,
    PriceSilver,
    PriceGold,
    PricePlatinum,

    /// <summary>"No value": the item cannot be sold.</summary>
    NoValue,
}

/// <summary>
/// Builds item tooltip lines in Terraria's order and wording (engine-independent so every UI and test shows the same
/// text): name with prefix and count, prefix- and player-adjusted combat stats, info lines, the description or buff
/// effect, buff duration, green and red prefix modifiers, the worn set bonus and the price.
/// </summary>
public static partial class ItemTooltip
{
    /// <summary>Lines without content or player context (unadjusted stats, default sell ratio).</summary>
    public static List<(string Text, TooltipKind Kind)> Lines(ItemStack stack, ItemUseInfo info, bool usedInRecipes) =>
        Lines(stack, info, TooltipContext.Plain(usedInRecipes));

    public static List<(string Text, TooltipKind Kind)> Lines(ItemStack stack, ItemUseInfo info, TooltipContext ctx)
    {
        var item = stack.Item!;
        var lines = new List<(string, TooltipKind)> { (NameLine(stack), TooltipKind.Name) };
        if (item.IsWeapon) WeaponLines(stack, info, ctx, lines);
        if (item.EquipSlot != EquipSlot.None) lines.Add(("Equipable", TooltipKind.Info));
        if (item.Category == ItemCategory.Vanity) lines.Add(("Vanity item", TooltipKind.Info));
        if (item.Defense > 0) lines.Add(($"{item.Defense} defense", TooltipKind.Stat));
        if (item.PickPower > 0) lines.Add(($"{item.PickPower}% pickaxe power", TooltipKind.Stat));
        if (item.AxePower > 0) lines.Add(($"{item.AxePower}% axe power", TooltipKind.Stat));
        if (item.HammerPower > 0) lines.Add(($"{item.HammerPower}% hammer power", TooltipKind.Stat));
        if (item.HealLife > 0) lines.Add(($"Restores {item.HealLife} life", TooltipKind.Stat));
        if (item.HealMana > 0) lines.Add(($"Restores {item.HealMana} mana", TooltipKind.Stat));
        if (item.ManaCost > 0) lines.Add(($"Uses {ManaCost(stack, ctx)} mana", TooltipKind.Stat));
        if (info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject)) lines.Add(("Can be placed", TooltipKind.Info));
        if (item.AmmoClass != null) lines.Add(("Ammo", TooltipKind.Info));
        if (item.Consumable && info.Has(ItemAction.Consume | ItemAction.SummonBoss)) lines.Add(("Consumable", TooltipKind.Info));
        if (ctx.UsedInRecipes) lines.Add(("Material", TooltipKind.Info));
        DescriptionLines(item, ctx, lines);
        ModifierLines(stack.Prefix, lines);
        SetBonusLine(item, ctx, lines);
        PriceLine(stack, ctx, lines);
        return lines;
    }

    /// <summary>"Legendary Muramasa", "Torch (25)".</summary>
    public static string NameLine(ItemStack stack)
    {
        var name = stack.Prefix != null ? $"{stack.Prefix.DisplayName} {stack.Item!.DisplayName}" : stack.Item!.DisplayName;
        return stack.Count > 1 ? $"{name} ({stack.Count})" : name;
    }

    /// <summary>Damage with the prefix and the player's class bonus (as <see cref="DamageModel"/> deals it).</summary>
    public static int Damage(ItemStack stack, TooltipContext ctx)
    {
        float damage = Prefixes.Damage(stack);
        if (ctx.Player != null) damage *= DamageModel.DamageMultiplier(ctx.Player.Effects.Stats, stack.Item!.DamageType);
        return (int)MathF.Round(damage);
    }

    /// <summary>Critical strike chance in percent: the base 4, the item's, the prefix's and the player's bonus.</summary>
    public static int CritChance(ItemStack stack, TooltipContext ctx) =>
        DamageModel.BaseCritChance + Prefixes.Crit(stack) + (ctx.Player != null ? DamageModel.CritBonus(ctx.Player.Effects.Stats, stack.Item!.DamageType) : 0);

    /// <summary>Use animation in ticks after the prefix's speed and, for swung and thrust items, the player's melee speed.</summary>
    public static int UseAnimation(ItemStack stack, ItemUseInfo info, TooltipContext ctx)
    {
        int ticks = Prefixes.UseTime(stack, stack.Item!.UseAnimation);
        if (ctx.Player == null || !info.Has(ItemAction.Melee | ItemAction.Thrust)) return ticks;
        float melee = 1f + ctx.Player.Effects.Stats.Bonus(StatIds.MeleeSpeed);
        return melee == 1f ? ticks : Math.Max(1, (int)MathF.Round(stack.Item.UseAnimation / MathF.Max(0.1f, Prefixes.UseSpeed(stack) * melee)));
    }

    private static int ManaCost(ItemStack stack, TooltipContext ctx) =>
        (int)MathF.Round(Prefixes.ManaCost(stack) * (ctx.Player?.Effects.Stats.Get(StatIds.ManaCost, 1f) ?? 1f));

    private static void WeaponLines(ItemStack stack, ItemUseInfo info, TooltipContext ctx, List<(string, TooltipKind)> lines)
    {
        var item = stack.Item!;
        var cls = item.DamageType == DamageClass.Generic ? "" : item.DamageType.ToString().ToLowerInvariant() + " ";
        lines.Add(($"{Damage(stack, ctx)} {cls}damage", TooltipKind.Stat));
        lines.Add(($"{CritChance(stack, ctx)}% critical strike chance", TooltipKind.Stat));
        lines.Add(($"{Speed(UseAnimation(stack, info, ctx))} speed", TooltipKind.Stat));
        lines.Add(($"{Knockback(Prefixes.Knockback(stack))} knockback", TooltipKind.Stat));
    }

    /// <summary>Use-speed descriptor from use animation ticks (the reference game's thresholds).</summary>
    public static string Speed(int ticks) => ticks switch
    {
        <= 8 => "Insanely fast",
        <= 20 => "Very fast",
        <= 25 => "Fast",
        <= 30 => "Average",
        <= 35 => "Slow",
        <= 45 => "Very slow",
        <= 55 => "Extremely slow",
        _ => "Snail",
    };

    public static string Knockback(float kb) => kb switch
    {
        <= 0 => "No",
        <= 1.5f => "Extremely weak",
        <= 3 => "Very weak",
        <= 4 => "Weak",
        <= 6 => "Average",
        <= 7 => "Strong",
        <= 9 => "Very strong",
        <= 11 => "Extremely strong",
        _ => "Insane",
    };
}
