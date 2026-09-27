using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Stats;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Combat;

/// <summary>A resolved hit.</summary>
public readonly record struct HitResult(int Damage, bool Crit, float Knockback);

/// <summary>
/// The reference game's damage model: class/generic damage bonuses, ±15 % variance, defense subtraction
/// scaled by difficulty, crits doubling damage, knockback reduced by resistance.
/// </summary>
public static class DamageModel
{
    public const int BaseCritChance = 4;

    public static float DefenseFactor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Expert => 0.75f,
        Difficulty.Master => 1f,
        _ => 0.5f,
    };

    /// <summary>Creature life and damage by world difficulty (the reference game doubles them in Expert, triples in Master).</summary>
    public static float EnemyScale(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Expert => 2f,
        Difficulty.Master => 3f,
        _ => 1f,
    };

    public static string DamageStat(DamageClass c) => c switch
    {
        DamageClass.Melee => StatIds.MeleeDamage,
        DamageClass.Ranged => StatIds.RangedDamage,
        DamageClass.Magic => StatIds.MagicDamage,
        DamageClass.Summon => StatIds.SummonDamage,
        _ => StatIds.GenericDamage,
    };

    public static string CritStat(DamageClass c) => c switch
    {
        DamageClass.Melee => StatIds.MeleeCrit,
        DamageClass.Ranged => StatIds.RangedCrit,
        DamageClass.Magic => StatIds.MagicCrit,
        _ => StatIds.GenericCrit,
    };

    /// <summary>Damage dealt by an attacker with stats <paramref name="attacker"/> (may be null for enemies).</summary>
    public static HitResult Roll(int baseDamage, DamageClass damageClass, int critChance, float knockback, StatSheet? attacker,
        int targetDefense, float defenseFactor, float targetEndurance, float targetKnockbackResist, ref Pcg32 rng)
    {
        float multiplier = 1f;
        int crit = critChance;
        if (attacker != null)
        {
            multiplier += attacker.Bonus(StatIds.GenericDamage) + (damageClass != DamageClass.Generic ? attacker.Bonus(DamageStat(damageClass)) : 0f);
            crit += (int)(attacker.Bonus(StatIds.GenericCrit) + attacker.Bonus(CritStat(damageClass)));
        }
        float raw = baseDamage * multiplier * rng.Range(0.85f, 1.15f);
        bool isCrit = damageClass != DamageClass.None && rng.NextInt(100) < crit;
        float afterDefense = MathF.Max(1f, raw - targetDefense * defenseFactor);
        if (isCrit) afterDefense *= 2f;
        afterDefense *= 1f - Math.Clamp(targetEndurance, 0f, 0.9f);
        int damage = Math.Max(1, (int)MathF.Round(afterDefense));
        float kb = knockback * (1f - Math.Clamp(targetKnockbackResist, 0f, 1f)) * (isCrit ? 1.4f : 1f);
        return new HitResult(damage, isCrit, kb);
    }
}
