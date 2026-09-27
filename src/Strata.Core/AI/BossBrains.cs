using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// Gel Sovereign: charges up big hops towards the target, slams down, sheds minion gels as it loses life and
/// teleports beside a target that runs too far (shrinking first as a telegraph, invulnerable meanwhile).
/// Phase: 0 = hopping, 1 = teleport shrink, 2 = reappear.
/// </summary>
public sealed class GelSovereignBrain : IBrain
{
    private readonly GelSovereignParams _p;

    public GelSovereignBrain(GelSovereignParams p) => _p = p;

    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        ShedMinions(npc, ctx);
        if (ctx.Target == null) return;
        npc.TargetPlayer = 0;
        if (npc.Phase == 1)
        {
            npc.Pose = "teleport";
            npc.Invulnerable = true;
            npc.Velocity = new Vector3(0, npc.Velocity.Y, 0);
            if (++npc.Timer < 50) return;
            // Reappear on the ground next to the target.
            var dir = BrainFactory.Flat(npc.Position - ctx.Target.Position);
            if (dir == Vector3.Zero) dir = Vector3.UnitX;
            npc.Position = ctx.Target.Position + dir * 5f + new Vector3(0, 3f, 0);
            npc.Velocity = Vector3.Zero;
            npc.Phase = 2;
            npc.Timer = 0;
            return;
        }
        if (npc.Phase == 2)
        {
            npc.Pose = "jump";
            if (++npc.Timer > 30)
            {
                npc.Phase = 0;
                npc.Invulnerable = false;
                npc.Timer = 0;
            }
            return;
        }
        if (ctx.TargetDistance > _p.TeleportDistance && npc.OnGround)
        {
            npc.Phase = 1;
            npc.Timer = 0;
            return;
        }
        if (!npc.OnGround)
        {
            npc.Pose = "jump";
            return;
        }
        npc.Velocity.X *= 0.5f;
        npc.Velocity.Z *= 0.5f;
        float life = npc.Life / (float)npc.MaxLife;
        int interval = (int)(_p.HopInterval * (0.6f + 0.4f * life)); // angrier when hurt
        npc.Pose = npc.Timer > interval - 20 ? "telegraph" : "idle";
        if (++npc.Timer < interval) return;
        npc.Timer = 0;
        npc.Counter++;
        bool big = npc.Counter % 3 == 0;
        var toTarget = BrainFactory.Flat(ctx.Target.Position - npc.Position);
        npc.Velocity = toTarget * _p.JumpSpeed * (big ? 1.4f : 1f) + new Vector3(0, BrainFactory.JumpVelocity(_p.JumpHeight * (big ? 1.6f : 1f)), 0);
        npc.Yaw = BrainFactory.YawOf(toTarget);
        npc.Pose = "jump";
    }

    private void ShedMinions(Npc npc, BrainContext ctx)
    {
        if (_p.Minion == null || ctx.Spawn == null || !ctx.Content.Enemies.TryGet(_p.Minion, out var minion)) return;
        float lost = 1f - npc.Life / (float)npc.MaxLife;
        int due = (int)(lost / _p.MinionEveryLife);
        while (npc.Counter2 < due)
        {
            npc.Counter2++;
            var offset = new Vector3(ctx.Rng.NextSigned(), 0.5f, ctx.Rng.NextSigned()) * 1.5f;
            if (ctx.Spawn(minion, npc.Center + offset) is { } m) m.Velocity = new Vector3(offset.X * 3f, 6f, offset.Z * 3f);
        }
    }
}

/// <summary>
/// The Watcher: phase 1 orbits the target summoning servant eyes, then telegraphs (stares) and dashes in bursts;
/// below a life fraction it transforms (phase 2 model) and dashes faster and more often.
/// Phase: 0 = orbit, 1 = telegraph, 2 = dash; <see cref="Npc.Counter"/> counts dashes in the burst.
/// </summary>
public sealed class WatcherBrain : IBrain
{
    private readonly WatcherParams _p;

    public WatcherBrain(WatcherParams p) => _p = p;

    public bool UsesGravity => false;

    public void Think(Npc npc, BrainContext ctx)
    {
        if (ctx.Target == null) return;
        npc.TargetPlayer = 0;
        bool enraged = npc.Life <= npc.MaxLife * _p.Phase2Life;
        npc.Enraged = enraged;
        if (enraged && npc.ModelOverride == null && _p.Phase2Model != null) npc.ModelOverride = _p.Phase2Model;
        var toTarget = ctx.Target.Center - npc.Center;
        npc.Timer++;
        switch (npc.Phase)
        {
            case 0:
            {
                float angle = npc.Age * (enraged ? 0.025f : 0.015f);
                float r = _p.OrbitRadius * Units.CellSize;
                var desired = ctx.Target.Center + new Vector3(MathF.Sin(angle) * r, r * 0.45f, MathF.Cos(angle) * r);
                var to = desired - npc.Center;
                var want = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) * MathF.Min(npc.Def.MoveSpeed * (enraged ? 1.5f : 1f), to.Length() * 2f) : Vector3.Zero;
                npc.Velocity += (want - npc.Velocity) * 0.08f;
                npc.Pose = "move";
                if (!enraged && _p.Servant != null && ctx.Spawn != null && npc.Timer % _p.ServantInterval == 0 && ctx.Content.Enemies.TryGet(_p.Servant, out var servant))
                    ctx.Spawn(servant, npc.Center + Vector3.Normalize(toTarget + new Vector3(0.01f)) * 1.5f);
                if (npc.Timer > (enraged ? 150 : 260))
                {
                    npc.Phase = 1;
                    npc.Timer = 0;
                    npc.Counter = 0;
                }
                break;
            }
            case 1: // telegraph: brake and stare
                npc.Velocity *= 0.85f;
                npc.Pose = "telegraph";
                if (npc.Timer >= (enraged ? _p.TelegraphTicks * 2 / 3 : _p.TelegraphTicks))
                {
                    npc.Phase = 2;
                    npc.Timer = 0;
                    npc.Velocity = Vector3.Normalize(toTarget + new Vector3(0.001f)) * _p.DashSpeed * (enraged ? 1.35f : 1f);
                }
                break;
            default: // dash
                npc.Pose = "attack";
                if (npc.Timer > 45)
                {
                    npc.Counter++;
                    npc.Timer = 0;
                    npc.Phase = npc.Counter >= _p.DashesPerBurst + (enraged ? 2 : 0) ? 0 : 1;
                }
                break;
        }
        if (toTarget.LengthSquared() > 1e-4f) npc.Yaw = BrainFactory.YawOf(toTarget);
    }
}
