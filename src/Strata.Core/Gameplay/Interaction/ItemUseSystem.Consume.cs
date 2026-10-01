using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    private UseOutcome Consume(PlayerEntity p, ItemUseInfo info)
    {
        var item = info.Item;
        var state = p.State;
        var cfg = info.Consume!;
        if (item.HealLife > 0 && _healingCooldown != null && state.Buffs.Has(_healingCooldown.Id)) return UseOutcome.Blocked;
        if (cfg.PermanentLife > 0 && Player.PlayerState.BaseLife + state.BonusLife >= cfg.LifeCap) return UseOutcome.Blocked;
        if (cfg.PermanentMana > 0 && Player.PlayerState.BaseMana + state.BonusMana >= cfg.ManaCap) return UseOutcome.Blocked;

        if (cfg.PermanentLife > 0) state.BonusLife += cfg.PermanentLife;
        if (cfg.PermanentMana > 0) state.BonusMana += cfg.PermanentMana;
        if (item.HealLife > 0)
        {
            state.Vitals.Heal(item.HealLife);
            if (_healingCooldown != null) state.Buffs.Add(_healingCooldown, _healingCooldown.Duration);
        }
        if (item.HealMana > 0)
        {
            state.Vitals.RestoreMana(item.HealMana);
            if (_manaCooldown != null) state.Buffs.Add(_manaCooldown, _manaCooldown.Duration);
        }
        if (item.Buff != null && _world.Session.Content.Buffs.TryGet(item.Buff.Id, out var buff))
            state.Buffs.Add(buff, item.Buff.Ticks, state.Effects.Immunities);
        state.Recompute();
        if (cfg.PermanentLife > 0) state.Vitals.Heal(cfg.PermanentLife);
        if (cfg.PermanentMana > 0) state.Vitals.RestoreMana(cfg.PermanentMana);
        if (cfg.Recall) Recall(p);
        if (cfg.Loot != null) Open(p, cfg.Loot);
        if (item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    /// <summary>Raised when a summon fails its conditions (message for the player).</summary>
    public event Action<PlayerEntity, string>? Refused;

    private UseOutcome Summon(PlayerEntity p, ItemUseInfo info)
    {
        if (info.StartEvent is { } start) return StartEvent(p, info, start);
        var cfg = info.Summon!;
        var content = _world.Session.Content;
        if (!content.Enemies.TryGet(cfg.Enemy, out var boss)) return UseOutcome.Blocked;
        if (_world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return UseOutcome.Blocked;
        var ctx = _world.Session.ContextAt(p.FeetCell);
        if (!info.SummonCondition!.Evaluate(ctx))
        {
            if (cfg.FailMessage != null) Refused?.Invoke(p, cfg.FailMessage);
            return UseOutcome.Blocked;
        }
        var flat = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(p.Aim.X, 0, p.Aim.Z) + new System.Numerics.Vector3(1e-4f, 0, 0));
        var at = p.Position + flat * cfg.Distance;
        if (!boss.Gravity) at.Y += 8f; // fliers arrive from above
        else if (boss.Boss is { Segments: > 0 }) at.Y -= 10f; // burrowers rise from below
        else at.Y += 6f; // hoppers drop in
        if (_world.Npcs.Spawn(boss, at) == null) return UseOutcome.Blocked;
        if (info.Item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private UseOutcome StartEvent(PlayerEntity p, ItemUseInfo info, Content.Specs.StartEventParams cfg)
    {
        if (!_world.Session.Content.Events.TryGet(cfg.Event, out var def) || _world.Events.IsActive(def.Id)) return UseOutcome.Blocked;
        if (!info.SummonCondition!.Evaluate(_world.Session.ContextAt(p.FeetCell)))
        {
            if (cfg.FailMessage != null) Refused?.Invoke(p, cfg.FailMessage);
            return UseOutcome.Blocked;
        }
        if (!_world.Events.Start(def)) return UseOutcome.Blocked;
        if (info.Item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private static void ConsumeHeld(PlayerEntity p, int count = 1)
    {
        var inv = p.State.Inventory;
        inv.Backpack.Take(inv.SelectedHotbar, Math.Max(1, count));
    }
}
