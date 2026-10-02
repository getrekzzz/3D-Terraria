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
        Spit(npc, ctx);
    }

    /// <summary>A random body segment spits at the target when it can see it (each piece of a split worm on its own clock).</summary>
    private void Spit(Npc npc, BrainContext ctx)
    {
        if (_p.SpitProjectile == null || ctx.Shoot == null || ctx.Target == null || npc.Segments.Count < 2) return;
        if (_p.SpitExpertOnly && !ctx.Expert) return;
        int interval = Math.Max(1, _p.SpitInterval);
        if (npc.Age == 0 || (npc.Age + (int)(npc.Id % interval)) % interval != 0) return;
        // A few random body segments (never the tail) are tried; the first that sees the target spits.
        for (int tries = 0; tries < 6; tries++)
        {
            var from = npc.Segments[ctx.Rng.NextInt(npc.Segments.Count - 1)];
            var aim = ctx.Target.Center - from;
            if (aim.LengthSquared() < 1e-4f || !(ctx.InSight?.Invoke(from, ctx.Target.Eye) ?? true)) continue;
            ctx.Shoot(_p.SpitProjectile, from, Vector3.Normalize(aim) * _p.SpitSpeed, _p.SpitDamage);
            return;
        }
    }

    /// <summary>Bit of <see cref="Npc.SegmentFlags"/>: this segment has launched its probe (it goes dark and stops firing).</summary>
    public const byte ProbeSpent = 1;

    /// <summary>
    /// Drones launched while fewer than the cap are alive. Each body segment (never the tail) carries one: a random segment
    /// that still has its drone launches it and is marked spent, so a long fight leaves the worm dark.
    /// </summary>
    private void ReleaseMinion(Npc npc, BrainContext ctx)
    {
        if (_p.Minion == null || npc.Segments.Count < 2) return;
        while (npc.SegmentFlags.Count < npc.Segments.Count) npc.SegmentFlags.Add(0); // marks the worm as a probe carrier
        if (ctx.Spawn == null || ctx.Target == null) return;
        if (npc.Age == 0 || npc.Age % Math.Max(1, _p.MinionInterval) != 0) return;
        if (!ctx.Content.Enemies.TryGet(_p.Minion, out var minion) || (ctx.CountAlive?.Invoke(minion.Id) ?? 0) >= _p.MaxMinions) return;
        var flags = npc.SegmentFlags;
        int body = npc.Segments.Count - 1, start = ctx.Rng.NextInt(body);
        for (int k = 0; k < body; k++)
        {
            int i = (start + k) % body;
            if ((flags[i] & ProbeSpent) != 0) continue;
            if (ctx.Spawn(minion, npc.Segments[i]) is not { } m) return; // the creature cap: the segment keeps its drone
            m.Velocity = new Vector3(ctx.Rng.NextSigned(), 1f, ctx.Rng.NextSigned()) * 4f;
            flags[i] |= ProbeSpent;
            return;
        }
    }

    /// <summary>Whether body segment <paramref name="i"/> has launched its drone.</summary>
    public static bool Spent(Npc npc, int i) => i < npc.SegmentFlags.Count && (npc.SegmentFlags[i] & ProbeSpent) != 0;

    /// <summary>
    /// Whether segment <paramref name="i"/> may fire a part's volley: on a probe carrier only body plates that still hold
    /// their probe (the tail never fires); any segment of other creatures.
    /// </summary>
    public static bool Fires(Npc npc, int i) => npc.SegmentFlags.Count == 0 || (i < npc.Segments.Count - 1 && !Spent(npc, i));

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
