using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Physics;

namespace Strata.Core.AI;

/// <summary>
/// Plantera's hook: a vine claw tethered to her (it dies with her) that latches onto a solid block near her target, then
/// every <see cref="PlanteraHookParams.RelocateInterval"/> ticks (± a quarter) lets go and creeps to a new one; Plantera's
/// leash keeps her near the hooks' centroid, so where they can grip limits where she can reach. The latch point lives in
/// <see cref="Npc.Direction"/>; <see cref="Npc.Phase"/> is 0 flying, 1 latched; <see cref="Npc.Timer"/> ticks to the next relocation.
/// </summary>
public sealed class PlanteraHookBrain : IBrain
{
    /// <summary>The 26 axis combinations a ray may take (no trigonometry: the rng picks one) and their lengths.</summary>
    private static readonly (int X, int Y, int Z, float Length)[] Rays = BuildRays();

    private readonly PlanteraHookParams _p;

    public PlanteraHookBrain(PlanteraHookParams p) => _p = p;

    public bool UsesGravity => false;

    public bool MovesFreely => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        var leader = npc.Leader;
        var around = ctx.Target?.Center ?? leader?.Center ?? npc.Center;
        bool enraged = leader is { Enraged: true }; // phase two: the vines creep faster
        if (--npc.Timer <= 0 || npc.Phase == 1 && !Solid(ctx, npc.Direction))
        {
            npc.Direction = Latch(ctx, around, _p.Reach);
            npc.Phase = 0;
            int interval = enraged ? _p.RelocateInterval * 2 / 3 : _p.RelocateInterval;
            npc.Timer = Math.Max(1, interval + ctx.Rng.NextInt(interval / 2 + 1) - interval / 4);
        }
        var to = npc.Direction - npc.Center;
        float dist = to.Length();
        float speed = _p.Speed * (enraged ? 1.5f : 1f);
        const float dt = 1f / Units.TicksPerSecond;
        if (dist <= speed * dt)
        {
            npc.Velocity = dist > 1e-4f ? to / dt : Vector3.Zero; // arrive exactly this tick
            npc.Phase = 1;
            npc.Pose = "idle";
        }
        else
        {
            npc.Velocity = to / dist * speed;
            npc.Phase = 0;
            npc.Pose = "move";
        }
        var look = (leader?.Center ?? around) - npc.Center;
        if (look.LengthSquared() > 1e-4f) npc.Yaw = BrainFactory.YawOf(-look); // the claw faces away from her
    }

    /// <summary>
    /// A point on a solid block within <paramref name="reach"/> m of <paramref name="from"/>: up to 12 random rays march cell by
    /// cell and the first to hit solid ground latches there (on the face it came from). Without any — an open arena — it
    /// falls back to the floor under the target, else to a point below it.
    /// </summary>
    public static Vector3 Latch(BrainContext ctx, Vector3 from, float reach)
    {
        var c = VoxelMotion.CellOf(from);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var r = Rays[ctx.Rng.NextInt(Rays.Length)];
            if (Cast(ctx, c, r.X, r.Y, r.Z, (int)(reach / (Units.CellSize * r.Length))) is { } hit) return hit;
        }
        if (Cast(ctx, c, 0, -1, 0, (int)(reach / Units.CellSize)) is { } floor) return floor;
        return from - new Vector3(0, reach * 0.5f, 0);
    }

    /// <summary>Marches from a cell along a step; the centre of the face of the first solid cell, or null.</summary>
    private static Vector3? Cast(BrainContext ctx, Int3 start, int dx, int dy, int dz, int steps)
    {
        int x = start.X, y = start.Y, z = start.Z;
        for (int i = 1; i <= steps; i++)
        {
            x += dx;
            y += dy;
            z += dz;
            if (!ctx.World.IsSolid(x, y, z)) continue;
            // The open side the ray came from: half a cell back from the solid cell's centre.
            var centre = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * Units.CellSize;
            return centre - new Vector3(dx, dy, dz) * (0.5f * Units.CellSize);
        }
        return null;
    }

    /// <summary>The latch point still has a solid block behind it (a mined block lets the hook go).</summary>
    private static bool Solid(BrainContext ctx, Vector3 latch)
    {
        var c = VoxelMotion.CellOf(latch);
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (ctx.World.IsSolid(c.X + dx, c.Y + dy, c.Z + dz)) return true;
        return false;
    }

    private static (int, int, int, float)[] BuildRays()
    {
        var rays = new List<(int, int, int, float)>(26);
        for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0 || z != 0) rays.Add((x, y, z, MathF.Sqrt(x * x + y * y + z * z)));
        return rays.ToArray();
    }
}
