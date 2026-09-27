using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// Gel behaviour: sits, then hops towards the target (or a random direction) every interval; every Nth hop is
/// bigger. Air control is minimal, like the reference game's slimes.
/// </summary>
public sealed class SlimeHopBrain : IBrain
{
    private readonly SlimeHopParams _p;

    public SlimeHopBrain(SlimeHopParams p) => _p = p;

    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        if (!npc.OnGround)
        {
            npc.Pose = "jump";
            return;
        }
        npc.Velocity.X *= 0.6f;
        npc.Velocity.Z *= 0.6f;
        npc.Pose = "idle";
        bool chasing = ctx.Target != null && ctx.TargetDistance < _p.AggroRange * Units.CellSize;
        if (++npc.Timer < (chasing ? _p.HopInterval * 2 / 3 : _p.HopInterval)) return;
        npc.Timer = ctx.Rng.NextInt(20);
        npc.Counter++;
        bool big = _p.BigHopEvery > 0 && npc.Counter % _p.BigHopEvery == 0;
        var dir = chasing ? BrainFactory.Flat(ctx.Target!.Position - npc.Position) : RandomDir(ref ctx.Rng);
        npc.TargetPlayer = chasing ? 0 : -1;
        float speed = _p.JumpSpeed * (big ? 1.3f : 1f);
        npc.Velocity = dir * speed + new Vector3(0, BrainFactory.JumpVelocity(_p.JumpHeight * (big ? 1.5f : 1f)), 0);
        if (dir != Vector3.Zero) npc.Yaw = BrainFactory.YawOf(dir);
        npc.Pose = "jump";
    }

    internal static Vector3 RandomDir(ref Determinism.Pcg32 rng)
    {
        float a = rng.NextFloat() * MathF.Tau;
        return new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
    }
}

/// <summary>
/// Ground walker (undead): walks straight at the target, jumps over ledges up to its jump height when blocked,
/// wanders when it has no target and turns away from the player in daylight if configured.
/// </summary>
public sealed class WalkerBrain : IBrain
{
    private readonly WalkerParams _p;

    public WalkerBrain(WalkerParams p) => _p = p;

    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        bool chasing = ctx.Target != null && ctx.TargetDistance < _p.AggroRange * Units.CellSize;
        Vector3 dir;
        if (chasing && !(_p.FleeDaylight && ctx.IsDay && ctx.OnSurface))
        {
            dir = BrainFactory.Flat(ctx.Target!.Position - npc.Position);
            npc.TargetPlayer = 0;
        }
        else
        {
            npc.TargetPlayer = -1;
            if (chasing) dir = -BrainFactory.Flat(ctx.Target!.Position - npc.Position); // flee the light
            else
            {
                if (--npc.Timer <= 0 || npc.Direction == Vector3.Zero)
                {
                    npc.Timer = 120 + ctx.Rng.NextInt(240);
                    npc.Direction = _p.Wander && ctx.Rng.Chance(0.7f) ? SlimeHopBrain.RandomDir(ref ctx.Rng) : Vector3.Zero;
                }
                dir = npc.Direction;
            }
        }
        float speed = npc.Def.MoveSpeed * (chasing ? 1f : 0.45f);
        var target = dir * speed;
        float accel = npc.OnGround ? 0.25f : 0.06f;
        npc.Velocity.X += (target.X - npc.Velocity.X) * accel;
        npc.Velocity.Z += (target.Z - npc.Velocity.Z) * accel;
        if (dir != Vector3.Zero) npc.Yaw = BrainFactory.YawOf(dir);
        npc.Pose = dir == Vector3.Zero ? "idle" : "move";
        // Blocked while trying to move: jump (once per landing).
        if (npc.OnGround && dir != Vector3.Zero && npc.Counter > 0)
        {
            npc.Velocity.Y = BrainFactory.JumpVelocity(_p.JumpHeight + 0.5f);
            npc.Counter = 0;
            npc.Pose = "jump";
        }
    }

    public void AfterMove(Npc npc, Vector3 intended)
    {
        // A horizontal component that collision zeroed means we walked into a wall: jump next tick.
        bool blocked = (MathF.Abs(intended.X) > 0.2f && MathF.Abs(npc.Velocity.X) < 1e-4f) ||
                       (MathF.Abs(intended.Z) > 0.2f && MathF.Abs(npc.Velocity.Z) < 1e-4f);
        npc.Counter = blocked ? 1 : 0;
    }
}

/// <summary>
/// Flyer (floating eyes, bats): drifts around the target at an orbit radius and periodically dashes through
/// it; bounces off terrain. Wanders slowly without a target.
/// </summary>
public sealed class FlyerBrain : IBrain
{
    private readonly FlyerParams _p;

    public FlyerBrain(FlyerParams p) => _p = p;

    public bool UsesGravity => false;

    public void Think(Npc npc, BrainContext ctx)
    {
        bool chasing = ctx.Target != null && ctx.TargetDistance < _p.AggroRange * Units.CellSize;
        npc.Timer++;
        if (chasing && _p.FleeDaylight && ctx.IsDay && ctx.OnSurface)
        {
            // Dawn: climb away from the player until far enough to despawn.
            npc.TargetPlayer = -1;
            npc.Phase = 0;
            var away = BrainFactory.Flat(npc.Center - ctx.Target!.Center);
            if (away == Vector3.Zero) away = Vector3.UnitX;
            Steer(npc, Vector3.Normalize(away + new Vector3(0, 0.6f, 0)) * npc.Def.MoveSpeed, 0.05f);
            npc.Yaw = BrainFactory.YawOf(away);
            npc.Pose = "move";
            return;
        }
        if (!chasing)
        {
            npc.TargetPlayer = -1;
            if (npc.Timer % 90 == 0 || npc.Direction == Vector3.Zero)
                npc.Direction = SlimeHopBrain.RandomDir(ref ctx.Rng) + new Vector3(0, (ctx.Rng.NextFloat() - 0.5f) * 0.6f, 0);
            Steer(npc, npc.Direction * npc.Def.MoveSpeed * 0.5f, 0.05f);
            npc.Pose = "move";
            return;
        }
        npc.TargetPlayer = 0;
        var target = ctx.Target!.Center;
        var toTarget = target - npc.Center;
        if (npc.Phase == 1)
        {
            // Dashing: keep momentum, end after a moment.
            if (npc.Timer > 40)
            {
                npc.Phase = 0;
                npc.Timer = 0;
            }
            npc.Pose = "attack";
        }
        else if (npc.Timer >= _p.DashCooldown && toTarget.Length() < _p.OrbitRadius * Units.CellSize * 1.6f)
        {
            npc.Phase = 1;
            npc.Timer = 0;
            npc.Velocity = Vector3.Normalize(toTarget) * _p.DashSpeed;
            npc.Pose = "attack";
        }
        else
        {
            float angle = npc.Id * 1.7f + npc.Age * 0.015f;
            float r = _p.OrbitRadius * Units.CellSize;
            var desired = target + new Vector3(MathF.Sin(angle) * r, r * 0.35f + MathF.Sin(npc.Age * 0.03f) * 0.8f, MathF.Cos(angle) * r);
            var to = desired - npc.Center;
            var want = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) * MathF.Min(npc.Def.MoveSpeed, to.Length() * 2f) : Vector3.Zero;
            Steer(npc, want, 0.06f);
            npc.Pose = "move";
        }
        if (toTarget.LengthSquared() > 1e-4f) npc.Yaw = BrainFactory.YawOf(toTarget);
    }

    private static void Steer(Npc npc, Vector3 desired, float rate) => npc.Velocity += (desired - npc.Velocity) * rate;

    public void AfterMove(Npc npc, Vector3 intended)
    {
        if (!_p.BounceOffTerrain) return;
        if (MathF.Abs(intended.X) > 0.1f && npc.Velocity.X == 0) npc.Velocity.X = -intended.X * 0.6f;
        if (MathF.Abs(intended.Y) > 0.1f && npc.Velocity.Y == 0) npc.Velocity.Y = -intended.Y * 0.6f;
        if (MathF.Abs(intended.Z) > 0.1f && npc.Velocity.Z == 0) npc.Velocity.Z = -intended.Z * 0.6f;
    }
}
