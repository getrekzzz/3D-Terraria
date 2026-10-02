using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Presentation;

namespace Strata.Core.Gameplay.Items;

public static partial class ItemTooltip
{
    /// <summary>How one prefix field reads: its value, the scale to the shown number (100 for fractions shown as
    /// percentages), the unit ("%" or none), the wording and whether a negative value is the good direction (mana cost).</summary>
    private readonly record struct Modifier(Func<PrefixDef, float> Value, float Scale, string Unit, string Text, bool LowerIsBetter = false);

    /// <summary>
    /// Every prefix field in Terraria's tooltip order and wording. Lines come from the fields, not the prefix ids, so
    /// any prefix table in data (Legendary, Warding, Menacing…) shows its modifiers without code changes.
    /// </summary>
    private static readonly Modifier[] Modifiers =
    {
        new(p => p.Damage, 100f, "%", "damage"),
        new(p => p.UseSpeed, 100f, "%", "speed"),
        new(p => p.Crit, 1f, "%", "critical strike chance"),
        new(p => p.ManaCost, 100f, "%", "mana cost", LowerIsBetter: true),
        new(p => p.Size, 100f, "%", "size"),
        new(p => p.Velocity, 100f, "%", "velocity"),
        new(p => p.Knockback, 100f, "%", "knockback"),
        new(p => p.Defense, 1f, "", "defense"),
        new(p => p.MaxMana, 1f, "", "mana"),
        new(p => p.MoveSpeed, 100f, "%", "movement speed"),
    };

    /// <summary>"+15% damage" in green, "-10% knockback" in red: one line per non-zero prefix field.</summary>
    private static void ModifierLines(PrefixDef? prefix, List<(string, TooltipKind)> lines)
    {
        if (prefix == null) return;
        foreach (var m in Modifiers)
        {
            int shown = (int)MathF.Round(m.Value(prefix) * m.Scale);
            if (shown == 0) continue;
            bool good = m.LowerIsBetter ? shown < 0 : shown > 0;
            lines.Add(($"{(shown > 0 ? "+" : "-")}{Math.Abs(shown)}{m.Unit} {m.Text}", good ? TooltipKind.Good : TooltipKind.Bad));
        }
    }

    /// <summary>
    /// The item's description; for a potion without one, its buff's effect ("Increase defense by 8"). A potion then
    /// states how long the buff lasts ("5 minute duration").
    /// </summary>
    private static void DescriptionLines(ItemDef item, TooltipContext ctx, List<(string, TooltipKind)> lines)
    {
        BuffDef? buff = null;
        if (item.Buff is { } grant && ctx.Content != null) ctx.Content.Buffs.TryGet(grant.Id, out buff);
        var text = !string.IsNullOrWhiteSpace(item.Description) ? item.Description : buff != null ? BuffText.Describe(buff, ctx.Content) : null;
        if (text != null)
            foreach (var line in text.Split('\n'))
                if (line.Length > 0) lines.Add((line, TooltipKind.Flavor));
        if (buff != null && item.Buff!.Ticks > 0 && !buff.Persistent) lines.Add((BuffDisplay.Duration(item.Buff.Ticks), TooltipKind.Stat));
    }

    /// <summary>The set bonus, shown only while the viewer wears the whole set this piece belongs to.</summary>
    private static void SetBonusLine(ItemDef item, TooltipContext ctx, List<(string, TooltipKind)> lines)
    {
        if (item.SetBonus == null || ctx.Player == null) return;
        var set = ctx.Player.Inventory.CompleteSet();
        for (int i = 0; i < set.Count; i++)
            if (set[i] == item)
            {
                lines.Add(($"Set bonus: {item.SetBonus}", TooltipKind.Info));
                return;
            }
    }

    /// <summary>
    /// "Sell price: 2 gold 40 silver" (or the shop's "Buy price"), coloured by its largest coin; "No value" when a
    /// merchant would pay nothing. Coins carry no price line (they are the money).
    /// </summary>
    private static void PriceLine(ItemStack stack, TooltipContext ctx, List<(string, TooltipKind)> lines)
    {
        if (stack.Item!.HasTag("coin")) return;
        long value = ctx.BuyPrice ?? Prefixes.SellValue(stack, ctx.SellRatio);
        if (value <= 0)
        {
            lines.Add(("No value", TooltipKind.NoValue));
            return;
        }
        lines.Add(($"{(ctx.BuyPrice != null ? "Buy" : "Sell")} price: {Coins.Format(value)}", PriceKind(value)));
    }

    /// <summary>The price line's kind for an amount (its largest coin).</summary>
    public static TooltipKind PriceKind(long copper) => Coins.TopTier(copper) switch
    {
        CoinTier.Platinum => TooltipKind.PricePlatinum,
        CoinTier.Gold => TooltipKind.PriceGold,
        CoinTier.Silver => TooltipKind.PriceSilver,
        _ => TooltipKind.PriceCopper,
    };
}
