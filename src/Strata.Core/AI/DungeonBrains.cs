using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>Dark casters: stand still facing the target (their ranged spec does the casting) and blink to a new spot.</summary>
public sealed class CasterBrain : IBrain
{
    private readonly CasterParams _p;

    public CasterBrain(CasterParams p) => _p = p;

    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        npc.Velocity.X *= 0.5f;
        npc.Velocity.Z *= 0.5f;
        var target = ctx.Target;
        if (target == null || ctx.TargetDistance > _p.AggroRange * Units.CellSize)
        {
            npc.Pose = "idle";
            return;
        }
        npc.TargetPlayer = 0;
        npc.Yaw = BrainFactory.YawOf(target.Position - npc.Position);
        npc.Timer++;
        if (npc.Phase == 1)
        {
            npc.Pose = "teleport";
            if (npc.Timer < 30) return;
            if (FindSpot(npc, ctx, target.Position) is { } feet)
            {
                npc.Position = feet;
                npc.Velocity = Vector3.Zero;
            }
            npc.Phase = 0;
            npc.Timer = 0;
            return;
        }
        npc.Pose = "idle";
        if (npc.Timer >= _p.TeleportInterval)
        {
            npc.Phase = 1;
            npc.Timer = 0;
        }
    }

    /// <summary>A standable spot (ground below, headroom above) at blink distance from the target.</summary>
    private Vector3? FindSpot(Npc npc, BrainContext ctx, Vector3 around)
    {
        int height = (int)MathF.Ceiling(npc.HalfExtents.Y * 2f / Units.CellSize);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float angle = ctx.Rng.NextFloat() * MathF.Tau, dist = ctx.Rng.Range(_p.MinBlink, _p.MaxBlink);
            var p = around + new Vector3(MathF.Sin(angle) * dist, 0, MathF.Cos(angle) * dist);
            int x = (int)MathF.Floor(p.X / Units.CellSize), z = (int)MathF.Floor(p.Z / Units.CellSize), y0 = (int)MathF.Floor(p.Y / Units.CellSize);
            for (int dy = 4; dy >= -6; dy--)
            {
                int y = y0 + dy;
                if (!ctx.World.IsSolid(x, y - 1, z)) continue;
                bool clear = true;
                for (int h = 0; h < height && clear; h++) clear = !ctx.World.IsSolid(x, y + h, z);
                if (clear) return new Vector3(x + 0.5f, y, z + 0.5f) * Units.CellSize;
            }
        }
        return null;
    }
}
