using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

public enum TooltipKind
{
    Name,
    Stat,
    Info,
    Flavor,
    Price,

    /// <summary>A prefix's stat change that helps ('+15% damage'; green).</summary>
    Modifier,

    /// <summary>A prefix's stat change that hurts ('-10% speed'; red).</summary>
    ModifierBad,
}

/// <summary>Builds item tooltip lines (engine-independent so every UI and test shows the same text).</summary>
public static class ItemTooltip
{
    public static List<(string Text, TooltipKind Kind)> Lines(ItemStack stack, ItemUseInfo info, bool usedInRecipes)
    {
        var item = stack.Item!;
        var lines = new List<(string, TooltipKind)> { (stack.Prefix != null ? $"{stack.Prefix.DisplayName} {item.DisplayName}" : item.DisplayName, TooltipKind.Name) };
        if (item.IsWeapon)
        {
            string kind = item.DamageType == DamageClass.Generic ? "" : item.DamageType.ToString().ToLowerInvariant() + " ";
            lines.Add(($"{Prefixes.Damage(stack)} {kind}damage", TooltipKind.Stat));
            lines.Add(($"{4 + Prefixes.Crit(stack)}% critical strike chance", TooltipKind.Stat));
            lines.Add(($"{Speed(Prefixes.UseTime(stack, item.UseAnimation))} speed", TooltipKind.Stat));
            lines.Add(($"{Knockback(Prefixes.Knockback(stack))} knockback", TooltipKind.Stat));
        }
        if (item.PickPower > 0) lines.Add(($"{item.PickPower}% pickaxe power", TooltipKind.Stat));
        if (item.AxePower > 0) lines.Add(($"{item.AxePower}% axe power", TooltipKind.Stat));
        if (item.HammerPower > 0) lines.Add(($"{item.HammerPower}% hammer power", TooltipKind.Stat));
        if (item.ManaCost > 0) lines.Add(($"Uses {item.ManaCost} mana", TooltipKind.Stat));
        if (item.EquipSlot != EquipSlot.None) lines.Add(("Equipable", TooltipKind.Info));
        if (item.Defense > 0) lines.Add(($"{item.Defense} defense", TooltipKind.Stat));
        if (item.SetBonus != null) lines.Add(($"Set bonus: {item.SetBonus}", TooltipKind.Info));
        if (item.HealLife > 0) lines.Add(($"Restores {item.HealLife} life", TooltipKind.Stat));
        if (item.HealMana > 0) lines.Add(($"Restores {item.HealMana} mana", TooltipKind.Stat));
        if (item.AmmoClass != null) lines.Add(("Ammo", TooltipKind.Info));
        if (info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject)) lines.Add(("Can be placed", TooltipKind.Info));
        if (item.Consumable && info.Has(ItemAction.Consume)) lines.Add(("Consumable", TooltipKind.Info));
        if (usedInRecipes) lines.Add(("Material", TooltipKind.Info));
        if (!string.IsNullOrEmpty(item.Description)) lines.Add((item.Description!, TooltipKind.Flavor));
        if (stack.Prefix is { } prefix) lines.AddRange(Modifiers(prefix));
        if (item.Value > 0) lines.Add(($"Sell price: {Coins.Format(Math.Max(1, Prefixes.Value(stack) / 5) * stack.Count)}", TooltipKind.Price));
        return lines;
    }

    /// <summary>A prefix's stat changes in the reference game's order ('+15% damage', '-10% speed', '+4 defense').</summary>
    public static IEnumerable<(string Text, TooltipKind Kind)> Modifiers(PrefixDef p)
    {
        static (string, TooltipKind) Line(float amount, string what, bool percent = true, bool lowerIsBetter = false)
        {
            string n = percent ? $"{MathF.Round(amount * 100f):+0;-0}%" : $"{amount:+0;-0}";
            return ($"{n} {what}", amount > 0 != lowerIsBetter ? TooltipKind.Modifier : TooltipKind.ModifierBad);
        }
        if (p.Defense != 0) yield return Line(p.Defense, "defense", percent: false);
        if (p.Damage != 0) yield return Line(p.Damage, "damage");
        if (p.UseSpeed != 0) yield return Line(p.UseSpeed, "speed");
        if (p.Crit != 0) yield return Line(p.Crit / 100f, "critical strike chance");
        if (p.ManaCost != 0) yield return Line(p.ManaCost, "mana cost", lowerIsBetter: true);
        if (p.Size != 0) yield return Line(p.Size, "size");
        if (p.Velocity != 0) yield return Line(p.Velocity, "velocity");
        if (p.Knockback != 0) yield return Line(p.Knockback, "knockback");
        if (p.MoveSpeed != 0) yield return Line(p.MoveSpeed, "movement speed");
        if (p.MeleeSpeed != 0) yield return Line(p.MeleeSpeed, "melee speed");
        if (p.MaxMana != 0) yield return Line(p.MaxMana, "mana", percent: false);
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
