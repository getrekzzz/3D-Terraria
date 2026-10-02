using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// Skeletron (and Skeletron Prime): a skull hovering over its target, faster for every hand it has lost, that spins
/// through walls at the target every <see cref="CryptWardenParams.SpinInterval"/> ticks with its defense changed
/// (lowered for Skeletron, doubled for Prime). Its hands and Prime's four arms are boss parts with their own brain
/// (<see cref="HandBrain"/>).
/// In Expert each living hand armours the skull, and once a hand falls or the skull is hurt it fires homing skulls.
/// While frenzied (its day rage, <see cref="Content.Defs.BossInfo.Enrage"/>) it never stops spinning.
/// Phase: 0 = hover, 1 = spin.
/// </summary>
public sealed class CryptWardenBrain : IBrain
{
    /// <summary>Most the skull turns per tick (rad): hovering right over the target must not make its hands whirl.</summary>
    private const float TurnRate = 0.06f;

    private readonly CryptWardenParams _p;

    public CryptWardenBrain(CryptWardenParams p) => _p = p;

    public bool UsesGravity => false;
    public bool MovesFreely => true;
    public void Think(Npc npc, BrainContext ctx)
    {
        int hands = HandsAlive(npc);
        int lost = Math.Max(0, (npc.Def.Boss?.Parts.Length ?? 0) - hands);
        float pace = 1f + _p.SpeedPerLostHand * lost;
        bool hurt = _p.Phase2Life > 0 && npc.Life <= npc.MaxLife * _p.Phase2Life;
        npc.Enraged = npc.Frenzied || hurt;
        var target = ctx.Target;
        if (target == null)
        {
            npc.Velocity += (new Vector3(0, 3f, 0) - npc.Velocity) * 0.05f;
            npc.Pose = "move";
            npc.Phase = 0;
            npc.Timer = 0;
            return;
        }
        npc.TargetPlayer = 0;
        npc.Timer++;
        var to = target.Center - npc.Center;
        if (npc.Phase == 0 && (npc.Frenzied || npc.Timer >= _p.SpinInterval))
        {
            npc.Phase = 1;
            npc.Timer = 0;
            ctx.Cue?.Invoke(npc, "boss_roar");
        }
        if (npc.Phase == 1)
        {
            npc.DefenseDelta += _p.SpinDefense;
            var dir = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) : Vector3.Zero;
            npc.Velocity += (dir * _p.SpinSpeed * pace - npc.Velocity) * 0.08f;
            Face(npc, to); // the skull's frame holds still so its hands stay at its sides; the spin is drawn
            npc.Pose = "spin";
            if (npc.Timer >= _p.SpinDuration && !npc.Frenzied)
            {
                npc.Phase = 0;
                npc.Timer = 0;
            }
            return;
        }
        Face(npc, to);
        var facing = new Vector3(MathF.Sin(npc.Yaw), 0, MathF.Cos(npc.Yaw));
        var side = new Vector3(facing.Z, 0, -facing.X);
        // Over the target, a little behind it along the facing (so the facing stays steady), swaying side to side.
        var desired = target.Center + new Vector3(0, _p.HoverHeight, 0) - facing * 1.5f + side * MathF.Sin(npc.Age * 0.02f) * 3f;
        var toDesired = desired - npc.Center;
        float speed = npc.Def.MoveSpeed * pace;
        var want = toDesired.LengthSquared() > 1e-4f ? Vector3.Normalize(toDesired) * MathF.Min(speed, toDesired.Length() * 1.5f) : Vector3.Zero;
        npc.Velocity += (want - npc.Velocity) * 0.06f;
        npc.Pose = "move";
        if (ctx.Expert) npc.DefenseDelta += _p.ExpertDefensePerHand * hands;
        Skulls(npc, ctx, target, hands, lost);
    }

    /// <summary>Living hands (boss parts) still on the skull.</summary>
    public static int HandsAlive(Npc npc)
    {
        int n = 0;
        for (int i = 0; i < npc.Followers.Count; i++)
            if (npc.Followers[i] is { Alive: true, IsPart: true }) n++;
        return n;
    }

    /// <summary>Turns towards the target's side at a limited rate; right overhead it keeps its facing.</summary>
    private static void Face(Npc npc, Vector3 to)
    {
        if (to.X * to.X + to.Z * to.Z < 0.25f) return;
        float want = MathF.Atan2(to.X, to.Z);
        float diff = MathF.IEEERemainder(want - npc.Yaw, MathF.Tau);
        npc.Yaw = MathF.IEEERemainder(npc.Yaw + Math.Clamp(diff, -TurnRate, TurnRate), MathF.Tau);
    }

    /// <summary>Expert: homing skulls once a hand has fallen or the skull is hurt, twice as often with no hands left.</summary>
    private void Skulls(Npc npc, BrainContext ctx, ICombatTarget target, int hands, int lost)
    {
        if (_p.Skull == null || !ctx.Expert || ctx.Shoot == null) return;
        if (lost == 0 && npc.Life >= npc.MaxLife * _p.SkullLife)
        {
            npc.Counter = 0;
            return;
        }
        int interval = Math.Max(1, hands == 0 ? _p.SkullInterval / 2 : _p.SkullInterval);
        if (++npc.Counter < interval) return;
        npc.Counter = interval; // out of sight it fires the moment the target shows again
        var from = npc.Center;
        var d = target.Center - from;
        if (d.LengthSquared() < 1e-4f || (ctx.InSight != null && !ctx.InSight(from, target.Eye))) return;
        ctx.Shoot(_p.Skull, from, Vector3.Normalize(d) * _p.SkullSpeed, _p.SkullDamage);
        ctx.Cue?.Invoke(npc, "skull_shot");
        npc.Counter = 0;
    }
}
