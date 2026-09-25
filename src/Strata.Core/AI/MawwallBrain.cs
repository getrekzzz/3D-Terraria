using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// The Mawwall: on its first tick it rises behind its target (on the side of the nearer world edge) and then sweeps
/// along X towards the far edge, filling the underworld cavity from the lava to the ceiling. The body is its mouth;
/// segments 0 and 1 are its eyes on the wall face; <see cref="Npc.Barrier"/> is the flesh the renderer draws and that
/// throws back anyone behind it. Phase: 0 = arriving, 1 = sweeping.
/// </summary>
public sealed class MawwallBrain : IBrain
{
    private readonly MawwallParams _p;

    public MawwallBrain(MawwallParams p) => _p = p;

    public bool UsesGravity => false;
    public bool MovesFreely => true;
    public bool PositionsSegments => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        var target = ctx.Target;
        float floor = (ctx.Bands.LavaLevel + 1) * Units.CellSize, ceiling = ctx.Bands.UnderworldCeiling * Units.CellSize;
        float mid = (floor + ceiling) * 0.5f;
        float worldX = ctx.World.SizeCells.X * Units.CellSize;
        if (npc.Phase == 0)
        {
            var from = target?.Position ?? npc.Position;
            float dir = from.X < worldX * 0.5f ? 1f : -1f; // travel towards the far side
            float x = Math.Clamp(from.X - dir * _p.StartDistance, 1f, worldX - 1f);
            npc.Direction = new Vector3(dir, 0, 0);
            npc.Position = new Vector3(x, mid - npc.HalfExtents.Y, from.Z);
            npc.Phase = 1;
        }
        float d = npc.Direction.X;
        float life = npc.MaxLife > 0 ? npc.Life / (float)npc.MaxLife : 1f;
        float speed = _p.BaseSpeed + (_p.MaxSpeed - _p.BaseSpeed) * (1f - life);
        float zVel = 0f;
        if (target != null)
        {
            npc.TargetPlayer = 0;
            if ((target.Position.X - npc.Position.X) * d > _p.CatchUpDistance) speed *= 2f;
            float dz = target.Position.Z - npc.Position.Z;
            zVel = Math.Clamp(dz * 2f, -_p.TrackSpeed, _p.TrackSpeed);
        }
        bool atEdge = d > 0 ? npc.Position.X >= worldX - 1f : npc.Position.X <= 1f;
        npc.Velocity = new Vector3(atEdge ? 0f : d * speed, (mid - npc.Center.Y) * 2f, zVel);
        npc.Yaw = BrainFactory.YawOf(npc.Direction);
        npc.Pose = "move";

        // The flesh, the eyes on its face and the mouth (the body) between them.
        var c = npc.Center;
        float half = _p.Thickness * 0.5f;
        npc.Barrier = (new Vector3(c.X - half, floor, c.Z - _p.Width * 0.5f), new Vector3(c.X + half, ceiling, c.Z + _p.Width * 0.5f));
        for (int i = 0; i < 2 && i < npc.Segments.Count; i++)
            npc.Segments[i] = new Vector3(c.X + d * half, c.Y + (i == 0 ? 1f : -1f) * _p.EyeSpacing * 0.5f, c.Z);

        npc.Timer++;
        if (_p.Minion == null || ctx.Spawn == null || npc.Timer % _p.MinionInterval != 0 || npc.Followers.Count >= _p.MaxMinions) return;
        if (!ctx.Content.Enemies.TryGet(_p.Minion, out var minion)) return;
        var offset = new Vector3(d * (half + 0.5f), ctx.Rng.Range(-_p.EyeSpacing, _p.EyeSpacing), ctx.Rng.Range(-_p.Width * 0.35f, _p.Width * 0.35f));
        if (ctx.Spawn(minion, c + offset) is { } m) NpcSystem.Tether(m, npc, offset);
    }
}
