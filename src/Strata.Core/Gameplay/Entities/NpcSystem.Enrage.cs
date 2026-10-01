using System.Numerics;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Messages;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Condition-driven boss rage (<see cref="Enrage"/>), announcements with a kind, and brain effect hooks.</summary>
public sealed partial class NpcSystem
{
    private readonly Dictionary<EnemyDef, ICondition> _enrage = new();
    private readonly ConditionContext _enrageContext = new();

    /// <summary>
    /// After the brain thinks: re-checks the boss's rage twice a second where its target stands (it stays set when
    /// latched), then applies the rage's damage and speed multipliers and fixed defense. The damage multiplier is applied
    /// only after a think (which reset <see cref="Npc.DamageScale"/>), so it never compounds while the creature reels.
    /// </summary>
    private void ApplyEnrage(Npc npc, ICombatTarget? target, bool thought)
    {
        if (npc.Def.Boss?.Enrage is not { } rage) return;
        if (target != null && npc.Age % 30 == 0 && !(rage.Latch && npc.Frenzied))
        {
            if (!_enrage.TryGetValue(npc.Def, out var condition))
                _enrage[npc.Def] = condition = ConditionCompiler.CompileAll(rage.When);
            var ctx = _world.Session.ContextAt(target.FeetCell, _enrageContext);
            ctx.PlayerDefense = (target as PlayerEntity)?.State.Defense ?? 0;
            bool was = npc.Frenzied;
            npc.Frenzied = condition.Evaluate(ctx);
            if (npc.Frenzied && !was && !string.IsNullOrEmpty(rage.Message)) Announce(rage.Message, MessageKind.Boss);
        }
        if (npc.Frenzied)
        {
            if (thought) npc.DamageScale *= rage.Damage;
            npc.SpeedScale = rage.Speed;
            npc.DefenseOverride = rage.SetDefense;
        }
        else
        {
            npc.SpeedScale = 1f;
            npc.DefenseOverride = null;
        }
    }

    /// <summary>
    /// Base damage of touching the creature (or its barrier): the enrage's fixed damage while frenzied. A boss part
    /// without a rage of its own that its brain marks frenzied (Skeletron's hands) uses its body's.
    /// </summary>
    internal int ContactBase(Npc npc)
    {
        var rage = npc.Def.Boss?.Enrage ?? (npc.IsPart ? npc.Leader!.Def.Boss?.Enrage : null);
        return npc.Frenzied && rage?.SetDamage is { } set ? set : (int)(npc.Def.Damage * npc.DamageScale * Scale);
    }

    /// <summary>Raises <see cref="Announced"/> and <see cref="Messaged"/> with the message's kind.</summary>
    private void Announce(string text, MessageKind kind)
    {
        Announced?.Invoke(text);
        Messaged?.Invoke(text, kind);
    }

    /// <summary>Gives a buff to every living player within <paramref name="radius"/> m of a point (their immunities hold).</summary>
    private void Pulse(Vector3 at, float radius, string buffId, int ticks)
    {
        if (!_world.Session.Content.Buffs.TryGet(buffId, out var buff)) return;
        foreach (var p in _world.Players)
        {
            if (!p.Alive || Vector3.DistanceSquared(p.Center, at) > radius * radius) continue;
            p.State.Buffs.Add(buff, ticks, p.State.Effects.Immunities);
            p.State.Recompute();
        }
    }
}
