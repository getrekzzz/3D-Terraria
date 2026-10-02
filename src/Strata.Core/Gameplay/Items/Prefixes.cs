using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Terraria's item modifiers (data/prefixes.json): which items may carry one, rolling one (crafting, loot, shop buys, the
/// Goblin Tinkerer's reforge), and the stats a stack ends up with. Swung melee weapons and tools take universal, common
/// and melee prefixes; other melee weapons (spears, yoyos, flails, boomerangs) only universal and common; ranged weapons
/// add ranged ones, magic and summon weapons magic ones; accessories take accessory prefixes.
/// </summary>
public static class Prefixes
{
    /// <summary>The prefix groups an item accepts (empty = it cannot carry a prefix).</summary>
    public static IReadOnlyList<string> Groups(ItemDef item)
    {
        if (item.MaxStack > 1 || item.Consumable || item.HasTag("placeholder")) return Array.Empty<string>();
        if (item.EquipSlot == EquipSlot.Accessory) return new[] { "accessory" };
        if (item.Damage <= 0 && !item.IsTool) return Array.Empty<string>();
        return item.DamageType switch
        {
            DamageClass.Ranged => new[] { "universal", "common", "ranged" },
            DamageClass.Magic or DamageClass.Summon => new[] { "universal", "common", "magic" },
            // Swords and shortswords (Terraria gives both the melee group: size and speed); spears stay common only.
            _ when item.IsTool || item.DamageType == DamageClass.Melee && UseStyleRules.Resolve(item) is UseStyle.Swing or UseStyle.Rapier => new[] { "universal", "common", "melee" },
            _ => new[] { "universal", "common" },
        };
    }

    /// <summary>Whether the prefix may sit on the item (placeholder prefixes or items from unknown save ids always may).</summary>
    public static bool Fits(ItemDef item, PrefixDef prefix) =>
        prefix.HasTag("placeholder") || item.HasTag("placeholder") || prefix.Applies.Any(Groups(item).Contains);

    public static bool CanHave(ItemDef item) => Groups(item).Count > 0;

    /// <summary>A random prefix for the item, weighted; null if it takes none (or none are defined for it).</summary>
    public static PrefixDef? Roll(ItemDef item, ContentDatabase content, ref Pcg32 rng, PrefixDef? not = null)
    {
        var groups = Groups(item);
        if (groups.Count == 0) return null;
        var pool = content.Prefixes.All.Where(p => p != not && !p.HasTag("placeholder") && p.Applies.Any(groups.Contains)).ToList();
        if (pool.Count == 0) return null;
        float total = pool.Sum(p => p.Weight);
        float pick = rng.NextFloat() * total;
        foreach (var p in pool)
            if ((pick -= p.Weight) <= 0) return p;
        return pool[^1];
    }

    /// <summary>A stack as it is made (crafted, dropped): three in four equipment pieces come out with a prefix.</summary>
    public static ItemStack Fresh(ItemDef item, int count, ContentDatabase content, ref Pcg32 rng)
    {
        if (!CanHave(item) || !rng.Chance(0.75f)) return new ItemStack(item, count);
        return new ItemStack(item, count, Roll(item, content, ref rng));
    }

    /// <summary>
    /// Gives a single unprefixed equipment piece its Fresh roll (loot drops, chest loot, shop buys). Draws from
    /// <paramref name="rng"/> only for items that can carry a prefix.
    /// </summary>
    public static void Apply(ref ItemStack stack, ContentDatabase content, ref Pcg32 rng)
    {
        if (stack.IsEmpty || stack.Count != 1 || stack.Prefix != null || !CanHave(stack.Item!)) return;
        stack = Fresh(stack.Item!, 1, content, ref rng);
    }

    /// <summary>
    /// Units of <paramref name="count"/> <paramref name="item"/>s as they are made: one stack, or for a prefixable
    /// (non-stacking) item a single per unit, each with its own <see cref="Apply"/> roll.
    /// </summary>
    public static List<ItemStack> Made(ItemDef item, int count, ContentDatabase content, ref Pcg32 rng)
    {
        if (!CanHave(item)) return new List<ItemStack> { new(item, count) };
        var units = new List<ItemStack>(count);
        for (int i = 0; i < count; i++)
        {
            var one = new ItemStack(item, 1);
            Apply(ref one, content, ref rng);
            units.Add(one);
        }
        return units;
    }

    public static int Damage(ItemStack s) => s.Prefix is { Damage: not 0 } p ? (int)MathF.Round(s.Item!.Damage * (1f + p.Damage)) : s.Item!.Damage;

    public static int Crit(ItemStack s) => s.Item!.Crit + (s.Prefix?.Crit ?? 0);

    public static float Knockback(ItemStack s) => s.Item!.Knockback * (1f + (s.Prefix?.Knockback ?? 0f));

    /// <summary>
    /// The prefix's use speed as a divisor of use time and animation: useSpeed is 1 - Terraria's use-time multiplier, so
    /// dividing by 1 / (1 - useSpeed) multiplies by that multiplier (Quick 0.9, Sluggish 1.2).
    /// </summary>
    public static float UseSpeed(ItemStack s) => 1f / (1f - (s.Prefix?.UseSpeed ?? 0f));

    /// <summary>Use time after the prefix's speed: Terraria's use-time multiplier (positive useSpeed = faster).</summary>
    public static int UseTime(ItemStack s, int useTime) => s.Prefix is { UseSpeed: not 0 } p ? Math.Max(1, (int)MathF.Round(useTime * (1f - p.UseSpeed))) : useTime;

    public static float ShootSpeed(ItemStack s) => s.Item!.ShootSpeed * (1f + (s.Prefix?.Velocity ?? 0f));

    public static float ManaCost(ItemStack s) => s.Item!.ManaCost * (1f + (s.Prefix?.ManaCost ?? 0f));

    /// <summary>Melee reach scale (the prefix's size).</summary>
    public static float Size(ItemStack s) => 1f + (s.Prefix?.Size ?? 0f);

    /// <summary>
    /// Value after the prefix (selling, reforging): Terraria's multiplier, damage x (2 - use time) x (2 - mana) x size x
    /// knockback x velocity x (1 + crit * 0.02), or an accessory prefix's value step, squared.
    /// </summary>
    public static long Value(ItemStack s)
    {
        var item = s.Item!;
        if (s.Prefix is not { } p) return item.Value;
        float mul = ValueMultiplier(p);
        return Math.Max(1, (long)MathF.Round(item.Value * Math.Max(0.1f, mul * mul)));
    }

    /// <summary>The prefix's value multiplier before squaring.</summary>
    public static float ValueMultiplier(PrefixDef p) => p.ValueMultiplier > 0 ? p.ValueMultiplier
        : (1f + p.Damage) * (1f + p.UseSpeed) * (1f - p.ManaCost) * (1f + p.Size) * (1f + p.Knockback) * (1f + p.Velocity) * (1f + p.Crit * 0.02f);

    /// <summary>
    /// What a merchant pays for the whole stack (copper): <paramref name="ratio"/> of the prefixed value per item, at
    /// least one copper for anything worth something (as in Terraria). The tooltip and the shop both use it.
    /// </summary>
    public static long SellValue(ItemStack s, float ratio = 0.2f)
    {
        if (s.IsEmpty) return 0;
        long each = Value(s);
        if (each <= 0) return 0;
        return Math.Max(1L, (long)Math.Floor(each * (double)ratio + 1e-6)) * s.Count;
    }

    /// <summary>Accessory prefixes as stat bonuses: (stat id, amount) pairs added to the wearer.</summary>
    public static IEnumerable<(string Stat, float Add)> AccessoryStats(PrefixDef p)
    {
        if (p.Defense != 0) yield return (Stats.StatIds.Defense, p.Defense);
        if (p.MoveSpeed != 0) yield return (Stats.StatIds.MoveSpeed, p.MoveSpeed);
        if (p.MeleeSpeed != 0) yield return (Stats.StatIds.MeleeSpeed, p.MeleeSpeed);
        if (p.Crit != 0) yield return (Stats.StatIds.GenericCrit, p.Crit); // percentage points
        if (p.Damage != 0) yield return (Stats.StatIds.GenericDamage, p.Damage);
        if (p.MaxMana != 0) yield return (Stats.StatIds.MaxMana, p.MaxMana);
    }
}
