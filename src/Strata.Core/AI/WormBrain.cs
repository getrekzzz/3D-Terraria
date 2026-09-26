using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Physics;

namespace Strata.Core.AI;

/// <summary>
/// Burrowing worm (Bore Tyrant, diggers): the head swims through terrain at constant speed, turning towards the
/// target at a limited rate and carving a real tunnel; when it bursts out above the target it arcs back down under
/// gravity with weak steering.
/// Body segments follow the head's path (see <see cref="NpcSystem"/>). Mechanical worms fly through terrain without
/// carving (dig radius 0, low air gravity) and release drones from their segments.
/// </summary>
public sealed class WormBrain : IBrain
{
    private readonly WormParams _p;

    public WormBrain(WormParams p) => _p = p;

    public bool UsesGravity => false;

    public bool MovesFreely => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        const float dt = 1f / Common.Units.TicksPerSecond;
        var head = npc.Center;
        bool buried = ctx.World.IsSolid(VoxelMotion.CellOf(head).X, VoxelMotion.CellOf(head).Y, VoxelMotion.CellOf(head).Z);
        var velocity = npc.Velocity.LengthSquared() > 1e-4f ? npc.Velocity : new Vector3(0, -_p.Speed, 0);
        // Out of the ground and above the target: a leap — gravity bends it back down. Crossing a cave below
        // the target it keeps swimming towards it.
        bool leaping = !buried && ctx.Target != null && head.Y > ctx.Target.Position.Y;
        if (ctx.Target != null)
        {
            npc.TargetPlayer = 0;
            var want = Vector3.Normalize(ctx.Target.Center - head + new Vector3(0, 0.001f, 0));
            float turn = _p.TurnRate * dt * (leaping ? 0.3f : 1f);
            var dir = Vector3.Normalize(velocity);
            dir = Vector3.Normalize(dir + (want - dir) * MathF.Min(1f, turn));
            velocity = dir * (leaping ? velocity.Length() : _p.Speed);
        }
        if (leaping) velocity.Y -= _p.AirGravity * dt;
        npc.Velocity = velocity;
        npc.Yaw = BrainFactory.YawOf(velocity);
        npc.Pose = buried ? "move" : "attack";
        if (_p.DigRadius > 0 && ctx.Carve != null && (npc.Age % _p.DigInterval) == 0 && (buried || NearTerrain(ctx, head)))
            ctx.Carve(head, _p.DigRadius);
        ReleaseMinion(npc, ctx);
    }

    /// <summary>Drones launched from a random segment while fewer than the cap are alive.</summary>
    private void ReleaseMinion(Npc npc, BrainContext ctx)
    {
        if (_p.Minion == null || ctx.Spawn == null || ctx.Target == null || npc.Segments.Count == 0) return;
        if (npc.Age == 0 || npc.Age % Math.Max(1, _p.MinionInterval) != 0) return;
        if (!ctx.Content.Enemies.TryGet(_p.Minion, out var minion) || (ctx.CountAlive?.Invoke(minion.Id) ?? 0) >= _p.MaxMinions) return;
        var from = npc.Segments[ctx.Rng.NextInt(npc.Segments.Count)];
        if (ctx.Spawn(minion, from) is { } m) m.Velocity = new Vector3(ctx.Rng.NextSigned(), 1f, ctx.Rng.NextSigned()) * 4f;
    }

    private static bool NearTerrain(BrainContext ctx, Vector3 point)
    {
        var c = VoxelMotion.CellOf(point);
        for (int dy = -2; dy <= 2; dy += 2)
            for (int dz = -2; dz <= 2; dz += 2)
                for (int dx = -2; dx <= 2; dx += 2)
                    if (ctx.World.IsSolid(c.X + dx, c.Y + dy, c.Z + dz)) return true;
        return false;
    }
}
