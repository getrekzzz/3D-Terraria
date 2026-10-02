using Strata.Core.Content.Defs;
using Strata.Core.Presentation;

namespace Strata.Core.Gameplay.Buffs;

public sealed class ActiveBuff
{
    public ActiveBuff(BuffDef def, int ticks)
    {
        Def = def;
        TicksLeft = ticks;
    }

    public BuffDef Def { get; }
    public int TicksLeft { get; set; }
}

/// <summary>Timed buffs/debuffs on an entity. Re-applying refreshes the duration to the longer value.</summary>
public sealed class BuffSet
{
    private readonly List<ActiveBuff> _buffs = new();

    public IReadOnlyList<ActiveBuff> Active => _buffs;

    public event Action? Changed;

    public void Add(BuffDef def, int ticks, ISet<string>? immunities = null)
    {
        if (immunities != null && immunities.Contains(def.Id)) return;
        var existing = _buffs.Find(b => b.Def == def);
        if (existing != null) existing.TicksLeft = Math.Max(existing.TicksLeft, ticks);
        else _buffs.Add(new ActiveBuff(def, ticks));
        Changed?.Invoke();
    }

    public bool Has(string id) => _buffs.Exists(b => b.Def.Id == id);

    public void Remove(string id)
    {
        if (_buffs.RemoveAll(b => b.Def.Id == id) > 0) Changed?.Invoke();
    }

    /// <summary>
    /// The player right-clicked the buff's icon: removes it and raises <see cref="Changed"/>, unless it is a debuff or a
    /// buff the player cannot shed (<see cref="BuffDisplay.CanCancel"/>) or is not active. The caller recomputes the
    /// stats (<c>PlayerState.Recompute</c>) when this returns true.
    /// </summary>
    public bool TryCancel(string id)
    {
        int index = _buffs.FindIndex(b => b.Def.Id == id);
        if (index < 0 || !BuffDisplay.CanCancel(_buffs[index].Def)) return false;
        _buffs.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Counts down one tick; returns true if any buff expired.</summary>
    public bool Tick()
    {
        foreach (var b in _buffs) b.TicksLeft--;
        bool expired = _buffs.RemoveAll(b => b.TicksLeft <= 0) > 0;
        if (expired) Changed?.Invoke();
        return expired;
    }

    public void ClearNonPersistent()
    {
        if (_buffs.RemoveAll(b => !b.Def.Persistent) > 0) Changed?.Invoke();
    }
}
