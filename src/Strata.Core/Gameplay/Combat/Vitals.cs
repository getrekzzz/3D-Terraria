namespace Strata.Core.Gameplay.Combat;

/// <summary>Life, mana, invulnerability frames and regeneration (fixed 60 Hz ticks).</summary>
public sealed class Vitals
{
    private float _lifeRegenAccumulator;
    private float _manaRegenAccumulator;

    public Vitals(int maxLife, int maxMana)
    {
        MaxLife = maxLife;
        Life = maxLife;
        MaxMana = maxMana;
        Mana = maxMana;
    }

    public int Life { get; set; }
    public int MaxLife { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }

    /// <summary>Remaining invulnerability ticks after being hit.</summary>
    public int ImmunityTicks { get; set; }

    public int TicksSinceHurt { get; private set; } = 10_000;
    public int TicksSinceManaUse { get; private set; } = 10_000;

    public bool IsDead => Life <= 0;

    /// <summary>Debug "god" mode (not saved): hits still land (knockback, effects) but life never drops below 1.</summary>
    public bool Undying { get; set; }

    private int Floor => Undying ? 1 : 0;

    /// <summary>Applies damage unless invulnerable. Returns false if the hit was ignored.</summary>
    public bool Hurt(int damage, int immunityTicks = 40)
    {
        if (ImmunityTicks > 0 || IsDead) return false;
        Life = Math.Max(Floor, Life - damage);
        ImmunityTicks = immunityTicks;
        TicksSinceHurt = 0;
        return true;
    }

    /// <summary>Damage that ignores invulnerability (debuffs, lava, falling).</summary>
    public void DamageDirect(int damage)
    {
        if (IsDead) return;
        Life = Math.Max(Floor, Life - damage);
        TicksSinceHurt = 0;
    }

    /// <summary>Kills outright, even when <see cref="Undying"/> (the kill command).</summary>
    public void Kill()
    {
        Life = 0;
        TicksSinceHurt = 0;
    }

    public void Heal(int amount) => Life = Math.Min(MaxLife, Life + Math.Max(0, amount));

    public bool TrySpendMana(int amount)
    {
        if (Mana < amount) return false;
        Mana -= amount;
        TicksSinceManaUse = 0;
        return true;
    }

    public void RestoreMana(int amount) => Mana = Math.Min(MaxMana, Mana + Math.Max(0, amount));

    /// <summary>One tick of natural regeneration; <paramref name="bonusLifePerSecond"/> comes from effects.</summary>
    public void Tick(float bonusLifePerSecond, bool suppressNatural = false)
    {
        if (ImmunityTicks > 0) ImmunityTicks--;
        TicksSinceHurt++;
        TicksSinceManaUse++;
        if (IsDead) return;
        float natural = suppressNatural ? 0f : Math.Clamp((TicksSinceHurt - 300) / 600f, 0f, 1f) * 2f + 0.25f;
        _lifeRegenAccumulator += (natural + bonusLifePerSecond) / 60f;
        if (_lifeRegenAccumulator >= 1f)
        {
            int n = (int)_lifeRegenAccumulator;
            _lifeRegenAccumulator -= n;
            Heal(n);
        }
        if (TicksSinceManaUse > 60)
        {
            _manaRegenAccumulator += (1f + MaxMana * 0.06f) / 60f;
            if (_manaRegenAccumulator >= 1f)
            {
                int n = (int)_manaRegenAccumulator;
                _manaRegenAccumulator -= n;
                RestoreMana(n);
            }
        }
    }
}
