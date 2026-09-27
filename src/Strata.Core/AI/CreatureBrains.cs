using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>Fish and jellyfish: swim toward targets inside liquid, drift otherwise, flop and sink on land.</summary>
public sealed class SwimmerBrain : IBrain
{
    private readonly SwimmerParams _p;

    public SwimmerBrain(SwimmerParams p) => _p = p;

    public bool UsesGravity => false;

    public void Think(Npc npc, BrainContext ctx)
    {
        const float dt = 1f / Units.TicksPerSecond;
        npc.Timer++;
        if (!InLiquid(ctx, npc.Center))
        {
            npc.Velocity.Y = MathF.Max(npc.Velocity.Y - BrainContext.Gravity * dt, -20f);
            npc.Velocity.X *= 0.95f;
            npc.Velocity.Z *= 0.95f;
            if (npc.OnGround && npc.Timer % 45 == 0)
                npc.Velocity = SlimeHopBrain.RandomDir(ref ctx.Rng) * 1.5f + new Vector3(0, 3.5f, 0);
            npc.Pose = "idle";
            return;
        }
        var target = ctx.Target;
        bool chase = target != null && ctx.TargetDistance < _p.AggroRange * Units.CellSize && (!_p.ChaseOnlyInLiquid || InLiquid(ctx, target.Center));
        Vector3 want;
        if (chase)
        {
            var to = target!.Center - npc.Center;
            want = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) * npc.Def.MoveSpeed : Vector3.Zero;
            npc.Yaw = BrainFactory.YawOf(to);
            npc.Pose = "attack";
        }
        else
        {
            if (npc.Timer % 120 == 0 || npc.Direction == Vector3.Zero)
                npc.Direction = SlimeHopBrain.RandomDir(ref ctx.Rng) + new Vector3(0, (ctx.Rng.NextFloat() - 0.5f) * 0.4f, 0);
            want = npc.Direction * npc.Def.MoveSpeed * 0.4f;
            npc.Yaw = BrainFactory.YawOf(npc.Direction);
            npc.Pose = "move";
        }
        if (_p.Pulse) want *= (npc.Timer % 50) < 15 ? 2.2f : 0.25f;
        // Stay under the surface.
        if (want.Y > 0 && !InLiquid(ctx, npc.Center + new Vector3(0, npc.HalfExtents.Y + 0.1f, 0))) want.Y = 0;
        npc.Velocity += (want - npc.Velocity) * (_p.Pulse ? 0.12f : 0.08f);
    }

    public static bool InLiquid(BrainContext ctx, Vector3 p) =>
        ctx.World.GetLiquid((int)MathF.Floor(p.X / Units.CellSize), (int)MathF.Floor(p.Y / Units.CellSize), (int)MathF.Floor(p.Z / Units.CellSize)) != 0;
}

/// <summary>A head on a stalk rooted at its spawn point: lunges at targets within reach, sways back otherwise.</summary>
public sealed class LungerBrain : IBrain
{
    private readonly LungerParams _p;

    public LungerBrain(LungerParams p) => _p = p;

    public bool UsesGravity => false;

    public bool MovesFreely => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        var root = npc.Anchor + new Vector3(0, 0.6f, 0);
        float reach = _p.Reach * Units.CellSize;
        var target = ctx.Target;
        Vector3 desired;
        float speed;
        if (target != null && Vector3.Distance(target.Center, root) < _p.AggroRange * Units.CellSize)
        {
            var to = target.Center - root;
            desired = root + (to.Length() > reach ? Vector3.Normalize(to) * reach : to);
            speed = npc.Def.MoveSpeed;
            npc.Pose = "attack";
        }
        else
        {
            float t = npc.Age * 0.03f + npc.Id;
            desired = root + new Vector3(MathF.Sin(t) * 0.4f, 1.2f + MathF.Sin(t * 1.7f) * 0.2f, MathF.Cos(t * 0.8f) * 0.4f);
            speed = npc.Def.MoveSpeed * 0.4f;
            npc.Pose = "idle";
        }
        var head = npc.Center;
        var delta = desired - head;
        npc.Velocity = delta.LengthSquared() > 1e-4f ? Vector3.Normalize(delta) * MathF.Min(speed, delta.Length() * 8f) : Vector3.Zero;
        var look = (target?.Center ?? desired) - head;
        if (look.LengthSquared() > 1e-4f) npc.Yaw = BrainFactory.YawOf(look);
    }
}
