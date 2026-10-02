using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// Item modifiers (Keen, Mythic, Warding…): which items may carry one, rolling one (crafting, loot, the Goblin
/// Tinkerer's reforge), and the stats a stack ends up with. Weapons and tools take their damage class's prefixes plus
/// the universal ones; accessories take accessory prefixes.
/// </summary>
public static class Prefixes
{
    /// <summary>The prefix groups an item accepts (empty = it cannot carry a prefix).</summary>
    public static IReadOnlyList<string> Groups(ItemDef item)
    {
        if (item.MaxStack > 1 || item.Consumable) return Array.Empty<string>();
        if (item.EquipSlot == EquipSlot.Accessory) return new[] { "accessory" };
        if (item.Damage <= 0) return Array.Empty<string>();
        bool tool = item.PickPower > 0 || item.AxePower > 0 || item.HammerPower > 0;
        var groups = new List<string> { "universal" };
        switch (item.DamageType)
        {
            case DamageClass.Melee: groups.Add("melee"); break;
            case DamageClass.Ranged: groups.Add("ranged"); break;
            case DamageClass.Magic: groups.Add("magic"); break;
            case DamageClass.Summon: groups.Add("summon"); break;
        }
        if (tool) groups.Add("tool");
        return groups;
    }

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

    public static int Damage(ItemStack s) => s.Prefix is { Damage: not 0 } p ? (int)MathF.Round(s.Item!.Damage * (1f + p.Damage)) : s.Item!.Damage;

    public static int Crit(ItemStack s) => s.Item!.Crit + (s.Prefix?.Crit ?? 0);

    public static float Knockback(ItemStack s) => s.Item!.Knockback * (1f + (s.Prefix?.Knockback ?? 0f));

    /// <summary>The prefix's use speed as a factor (Brisk 1.1, Sluggish 0.8): it divides use time and animation alike.</summary>
    public static float UseSpeed(ItemStack s) => 1f + (s.Prefix?.UseSpeed ?? 0f);

    /// <summary>Use time after the prefix's speed (positive = faster).</summary>
    public static int UseTime(ItemStack s, int useTime) => s.Prefix is { UseSpeed: not 0 } ? Math.Max(1, (int)MathF.Round(useTime / UseSpeed(s))) : useTime;

    public static float ShootSpeed(ItemStack s) => s.Item!.ShootSpeed * (1f + (s.Prefix?.Velocity ?? 0f));

    public static float ManaCost(ItemStack s) => s.Item!.ManaCost * (1f + (s.Prefix?.ManaCost ?? 0f));

    /// <summary>Melee reach scale (the prefix's size).</summary>
    public static float Size(ItemStack s) => 1f + (s.Prefix?.Size ?? 0f);

    /// <summary>Value after the prefix: better modifiers are worth more (used for selling and reforging).</summary>
    public static long Value(ItemStack s)
    {
        var item = s.Item!;
        if (s.Prefix is not { } p) return item.Value;
        float mul = p.ValueMultiplier > 0 ? p.ValueMultiplier
            : 1f + p.Damage * 2f + p.UseSpeed * 2f + p.Crit * 0.02f + p.Knockback * 0.5f + p.Size * 0.5f + p.Velocity * 0.5f - p.ManaCost * 1.5f
              + p.Defense * 0.1f + p.MoveSpeed * 2f + p.MaxMana * 0.004f;
        return Math.Max(1, (long)MathF.Round(item.Value * Math.Max(0.1f, mul * mul)));
    }

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
        if (p.Crit != 0) yield return (Stats.StatIds.GenericCrit, p.Crit); // percentage points
        if (p.Damage != 0) yield return (Stats.StatIds.GenericDamage, p.Damage);
        if (p.MaxMana != 0) yield return (Stats.StatIds.MaxMana, p.MaxMana);
    }
}
