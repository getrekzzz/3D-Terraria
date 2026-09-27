using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

public enum TooltipKind
{
    Name,
    Stat,
    Info,
    Flavor,
    Price,
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
            lines.Add(($"{item.Damage} {item.DamageType.ToString().ToLowerInvariant()} damage", TooltipKind.Stat));
            lines.Add(($"{4 + item.Crit}% critical strike chance", TooltipKind.Stat));
            lines.Add(($"{Speed(item.UseAnimation)} speed", TooltipKind.Stat));
            lines.Add(($"{Knockback(item.Knockback)} knockback", TooltipKind.Stat));
        }
        if (item.PickPower > 0) lines.Add(($"{item.PickPower}% pickaxe power", TooltipKind.Stat));
        if (item.AxePower > 0) lines.Add(($"{item.AxePower}% axe power", TooltipKind.Stat));
        if (item.HammerPower > 0) lines.Add(($"{item.HammerPower}% hammer power", TooltipKind.Stat));
        if (item.ManaCost > 0) lines.Add(($"Uses {item.ManaCost} mana", TooltipKind.Stat));
        if (item.EquipSlot != EquipSlot.None) lines.Add(("Equipable", TooltipKind.Info));
        if (item.Defense > 0) lines.Add(($"{item.Defense} defense", TooltipKind.Stat));
        if (item.HealLife > 0) lines.Add(($"Restores {item.HealLife} life", TooltipKind.Stat));
        if (item.HealMana > 0) lines.Add(($"Restores {item.HealMana} mana", TooltipKind.Stat));
        if (item.AmmoClass != null) lines.Add(("Ammo", TooltipKind.Info));
        if (info.Has(ItemAction.PlaceBlock) || info.Has(ItemAction.PlaceObject)) lines.Add(("Can be placed", TooltipKind.Info));
        if (item.Consumable && info.Has(ItemAction.Consume)) lines.Add(("Consumable", TooltipKind.Info));
        if (usedInRecipes) lines.Add(("Material", TooltipKind.Info));
        if (!string.IsNullOrEmpty(item.Description)) lines.Add((item.Description!, TooltipKind.Flavor));
        if (item.Value > 0) lines.Add(($"Sell price: {Coins.Format(Math.Max(1, item.Value / 5) * stack.Count)}", TooltipKind.Price));
        return lines;
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
