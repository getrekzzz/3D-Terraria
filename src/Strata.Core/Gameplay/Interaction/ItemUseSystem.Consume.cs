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
        if (item.HealLife > 0 && _healingCooldown != null && state.Buffs.Has(_healingCooldown.Id)) return Refuse(_healingCooldown.DisplayName);
        if (cfg.PermanentLife > 0 && Player.PlayerState.BaseLife + state.BonusLife >= cfg.LifeCap) return UseOutcome.Blocked;
        if (cfg.PermanentMana > 0 && Player.PlayerState.BaseMana + state.BonusMana >= cfg.ManaCap) return UseOutcome.Blocked;
        if (cfg.ExtraAccessorySlot && (state.ExtraAccessorySlot || _world.Session.Settings.Difficulty is not (World.Difficulty.Expert or World.Difficulty.Master)))
            return UseOutcome.Blocked;
        if (cfg.MinecartUpgrade && state.MinecartUpgrade) return UseOutcome.Blocked;
        if (cfg.ExtraAccessorySlot)
        {
            state.ExtraAccessorySlot = true;
            _world.UnlockAccessorySlots(state); // the new slot shows at once
        }
        if (cfg.MinecartUpgrade) state.MinecartUpgrade = true;

        if (cfg.PermanentLife > 0) state.BonusLife += cfg.PermanentLife;
        if (cfg.PermanentMana > 0) state.BonusMana += cfg.PermanentMana;
        int life = 0, mana = 0;
        if (item.HealLife > 0)
        {
            life += state.Vitals.Heal(item.HealLife);
            if (_healingCooldown != null) state.Buffs.Add(_healingCooldown, _healingCooldown.Duration);
        }
        if (item.HealMana > 0)
        {
            mana += state.Vitals.RestoreMana(item.HealMana);
            if (_manaCooldown != null) state.Buffs.Add(_manaCooldown, _manaCooldown.Duration);
        }
        if (item.Buff != null && _world.Session.Content.Buffs.TryGet(item.Buff.Id, out var buff))
            state.Buffs.Add(buff, item.Buff.Ticks, state.Effects.Immunities);
        state.Recompute();
        // Crystals fill the life or mana they add, and float it like a potion does (Terraria's HealEffect / ManaEffect).
        if (cfg.PermanentLife > 0) life += state.Vitals.Heal(cfg.PermanentLife);
        if (cfg.PermanentMana > 0) mana += state.Vitals.RestoreMana(cfg.PermanentMana);
        _world.ReportHeal(p, life, mana: false);
        _world.ReportHeal(p, mana, mana: true);
        if (cfg.Recall) Recall(p);
        if (cfg.Loot != null) Open(p, cfg.Loot);
        if (item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private UseOutcome Summon(PlayerEntity p, ItemUseInfo info)
    {
        if (info.StartEvent is { } start) return StartEvent(p, info, start);
        var cfg = info.Summon!;
        var content = _world.Session.Content;
        if (!content.Enemies.TryGet(cfg.Enemy, out var boss)) return UseOutcome.Blocked;
        if (_world.Npcs.All.Any(n => n.Def == boss && n.Alive)) return UseOutcome.Blocked;
        var ctx = _world.Session.ContextAt(p.FeetCell);
        if (!info.SummonCondition!.Evaluate(ctx)) return cfg.FailMessage != null ? Refuse(cfg.FailMessage) : UseOutcome.Blocked;
        if (cfg.Curse != null) return CurseResident(p, info, cfg, boss);
        var flat = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(p.Aim.X, 0, p.Aim.Z) + new System.Numerics.Vector3(1e-4f, 0, 0));
        var at = p.Position + flat * cfg.Distance;
        if (!boss.Gravity) at.Y += 8f; // fliers arrive from above
        else if (boss.Boss is { Segments: > 0 }) at.Y -= 10f; // burrowers rise from below
        else at.Y += 6f; // hoppers drop in
        if (_world.Npcs.Spawn(boss, at) == null) return UseOutcome.Blocked;
        if (info.Item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private UseOutcome CurseResident(PlayerEntity p, ItemUseInfo info, Content.Specs.SummonBossParams cfg, Content.Defs.EnemyDef boss)
    {
        var victim = _world.Town.Residents.FirstOrDefault(r => r.Alive && r.Def.Id == cfg.Curse);
        if (victim == null) return cfg.CurseFailMessage != null ? Refuse(cfg.CurseFailMessage) : UseOutcome.Blocked;
        if (!_world.Commerce.Curse(victim, boss, cfg.Height)) return UseOutcome.Blocked;
        if (info.Item.Consumable) ConsumeHeld(p);
        return UseOutcome.Consumed;
    }

    private UseOutcome StartEvent(PlayerEntity p, ItemUseInfo info, Content.Specs.StartEventParams cfg)
    {
        if (!_world.Session.Content.Events.TryGet(cfg.Event, out var def) || _world.Events.IsActive(def.Id)) return UseOutcome.Blocked;
        if (!info.SummonCondition!.Evaluate(_world.Session.ContextAt(p.FeetCell))) return cfg.FailMessage != null ? Refuse(cfg.FailMessage) : UseOutcome.Blocked;
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
