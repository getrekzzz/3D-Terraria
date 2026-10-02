using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Touching a creature hurts: contact damage, knockback and contact effects on players.</summary>
public sealed partial class NpcSystem
{
    private void ContactDamage(Npc npc, PlayerEntity player)
    {
        if (!player.Alive || player.State.Vitals.ImmunityTicks > 0 || npc.Def.Damage <= 0) return;
        foreach (var tag in player.State.Effects.Pacified)
            if (npc.Def.HasTag(tag)) return;
        var pBox = (player.Center - PlayerEntity.HalfExtents, player.Center + PlayerEntity.HalfExtents);
        int volume = Touched(npc, pBox);
        if (volume < 0) return;
        var state = player.State;
        var hit = DamageModel.Roll(_world.Banners.Taken(player, npc, ContactOf(npc, volume)), DamageClass.None, 0, 6f, null, state.Defense,
            DamageModel.DefenseFactor(_world.Session.Settings.EffectiveDifficulty), state.Effects.Stats.Bonus(Stats.StatIds.EnduranceReduction),
            state.Effects.Stats.Bonus(Stats.StatIds.KnockbackResist), ref _rng);
        if (!state.Vitals.Hurt(hit.Damage, PlayerHitImmunity + (int)state.Effects.Stats.Bonus(Stats.StatIds.Immunity))) return;
        var away = BrainFactory.Flat(player.Position - npc.Position);
        if (away == Vector3.Zero) away = new Vector3(MathF.Sin(npc.Yaw), 0, MathF.Cos(npc.Yaw));
        // Terraria-like: a clear shove away from the creature with a hop (none at full knockback resistance).
        float stand = 1f - Math.Clamp(state.Effects.Stats.Bonus(Stats.StatIds.KnockbackResist), 0f, 1f);
        var knock = (away * (4f + hit.Knockback * 0.6f) + new Vector3(0, 5.5f, 0)) * stand;
        ReportPlayerDamage(player, hit.Damage, knock);
        ApplyContactEffects(npc, player);
        float thorns = state.Effects.Stats.Bonus(Stats.StatIds.Thorns);
        if (thorns > 0) Hit(npc, null, (int)(npc.Def.Damage * npc.DamageScale * thorns), DamageClass.None, 0, 0f, player.Center, melee: false);
    }

    /// <summary>
    /// The first of the creature's volumes overlapping a box: 0 = body (head), i + 1 = segment i, −1 = none;
    /// allocation-free (runs every tick).
    /// </summary>
    private static int Touched(Npc npc, (Vector3 Min, Vector3 Max) box)
    {
        if (Npc.Overlaps(npc.Box, box)) return 0;
        if (npc.Segments.Count == 0) return -1;
        var half = new Vector3(npc.Def.Boss?.SegmentRadius ?? 0.5f);
        for (int i = 0; i < npc.Segments.Count; i++)
            if (Npc.Overlaps((npc.Segments[i] - half, npc.Segments[i] + half), box)) return i + 1;
        return -1;
    }

    /// <summary>
    /// Announces damage a player took from any source (numbers, knockback, hurt effects); <paramref name="kind"/> is
    /// <see cref="DamageKind.Periodic"/> for debuffs ticking, which show smaller numbers and a quieter hurt sound.
    /// </summary>
    public void ReportPlayerDamage(PlayerEntity player, int damage, Vector3 knock, DamageKind kind = DamageKind.Player)
    {
        Damaged?.Invoke(new DamageEvent(player.Center + new Vector3(0, 1f, 0), damage, false, true, 0, kind, player));
        PlayerHurt?.Invoke(player, damage, knock);
    }
}
