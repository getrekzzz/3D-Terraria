using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Stats;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Grown around the player's box so standing on or brushing against a spike counts as touching it.</summary>
    private const float HazardReach = 0.05f;

    private bool? _hasHazards;

    /// <summary>
    /// Objects with a hazard (dungeon spikes) bite a player touching them: the hit rolls against defense, waits out the
    /// immunity frames like any other, and knocks the player away from the spike and upward so they can jump clear.
    /// </summary>
    private void TouchHazards(PlayerEntity p)
    {
        _hasHazards ??= Session.Content.TileObjects.All.Any(d => d.Hazard != null);
        var state = p.State;
        if (_hasHazards == false || state.Vitals.ImmunityTicks > 0) return;
        var objects = Session.World.TileObjects;
        var lo = (p.Center - PlayerEntity.HalfExtents - new Vector3(HazardReach)) / Units.CellSize;
        var hi = (p.Center + PlayerEntity.HalfExtents + new Vector3(HazardReach)) / Units.CellSize;
        for (int y = (int)MathF.Floor(lo.Y); y <= (int)MathF.Floor(hi.Y); y++)
            for (int z = (int)MathF.Floor(lo.Z); z <= (int)MathF.Floor(hi.Z); z++)
                for (int x = (int)MathF.Floor(lo.X); x <= (int)MathF.Floor(hi.X); x++)
                {
                    if (objects.At(new Int3(x, y, z)) is not { Def.Hazard: { } hazard } obj) continue;
                    var rng = new Pcg32(Hash.Combine((ulong)obj.Id, Session.Clock.Tick));
                    var hit = DamageModel.Roll(hazard.Damage, DamageClass.None, 0, hazard.Knockback, null, state.Defense,
                        DamageModel.DefenseFactor(Session.Settings.EffectiveDifficulty), state.Effects.Stats.Bonus(StatIds.EnduranceReduction),
                        state.Effects.Stats.Bonus(StatIds.KnockbackResist), ref rng);
                    if (!state.Vitals.Hurt(hit.Damage, NpcSystem.PlayerHitImmunity + (int)state.Effects.Stats.Bonus(StatIds.Immunity))) return;
                    var spike = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * Units.CellSize;
                    var away = new Vector3(p.Center.X - spike.X, 0, p.Center.Z - spike.Z);
                    away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.Zero;
                    Npcs.ReportPlayerDamage(p, hit.Damage, away * (2f + hit.Knockback * 0.5f) + new Vector3(0, 4f, 0),
                        new Harm(HarmKind.Trap, Harm.WithArticle(obj.Def.DisplayName), obj.Def.Id));
                    return;
                }
    }
}
