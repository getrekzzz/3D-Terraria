namespace Strata.Core.Gameplay.Stats;

/// <summary>Additive and multiplicative modifiers per stat (from equipment, buffs, prefixes).</summary>
public sealed class StatSheet
{
    private readonly Dictionary<string, (float Add, float Mul)> _mods = new(StringComparer.Ordinal);

    public void Add(string stat, float add, float mul = 1f)
    {
        var (a, m) = _mods.TryGetValue(stat, out var cur) ? cur : (0f, 1f);
        _mods[stat] = (a + add, m * mul);
    }

    /// <summary>(base + add) × mul.</summary>
    public float Get(string stat, float baseValue)
    {
        if (!_mods.TryGetValue(stat, out var m)) return baseValue;
        return (baseValue + m.Add) * m.Mul;
    }

    /// <summary>Additive bonus only (percentage stats: 0.1 = +10 %).</summary>
    public float Bonus(string stat) => _mods.TryGetValue(stat, out var m) ? m.Add : 0f;

    public float Multiplier(string stat) => _mods.TryGetValue(stat, out var m) ? m.Mul : 1f;

    public void Clear() => _mods.Clear();
}
