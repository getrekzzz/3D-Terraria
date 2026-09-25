using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// The Crypt Warden: a skull hovering over its target whose two hands (segments 0 and 1) flank it and take turns
/// swiping at the target; every so often it spins and charges straight through walls. By day it is enraged
/// (<see cref="Content.Defs.BossInfo.DayEnrage"/>). Phase: 0 = hover, 1 = spin.
/// </summary>
public sealed class CryptWardenBrain : IBrain
{
    private readonly CryptWardenParams _p;

    public CryptWardenBrain(CryptWardenParams p) => _p = p;

    public bool UsesGravity => false;
    public bool MovesFreely => true;
    public bool PositionsSegments => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        var target = ctx.Target;
        var rage = npc.Def.Boss?.DayEnrage;
        bool enraged = rage != null && ctx.IsDay;
        float speed = npc.Def.MoveSpeed * (enraged ? rage!.Speed : 1f);
        npc.DamageScale = enraged ? rage!.Damage : 1f;
        if (target == null)
        {
            npc.Velocity += (new Vector3(0, 3f, 0) - npc.Velocity) * 0.05f;
            PlaceHands(npc, npc.Center + Vector3.UnitX, 0.5f, -1, 0);
            return;
        }
        npc.TargetPlayer = 0;
        npc.Timer++;
        var to = target.Center - npc.Center;
        if (npc.Phase == 1)
        {
            var dir = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) : Vector3.Zero;
            npc.Velocity += (dir * _p.SpinSpeed * (enraged ? rage!.Speed : 1f) - npc.Velocity) * 0.08f;
            npc.Yaw += 0.35f;
            npc.Pose = "attack";
            PlaceHands(npc, target.Center, 0.45f, -1, 0);
            if (npc.Timer >= _p.SpinDuration)
            {
                npc.Phase = 0;
                npc.Timer = 0;
            }
            return;
        }
        var flat = BrainFactory.Flat(to);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flat == Vector3.Zero ? Vector3.UnitZ : flat));
        var desired = target.Center + new Vector3(0, _p.HoverHeight, 0) + side * MathF.Sin(npc.Age * 0.02f) * 3f;
        var toDesired = desired - npc.Center;
        var want = toDesired.LengthSquared() > 1e-4f ? Vector3.Normalize(toDesired) * MathF.Min(speed, toDesired.Length() * 1.5f) : Vector3.Zero;
        npc.Velocity += (want - npc.Velocity) * 0.06f;
        npc.Yaw = BrainFactory.YawOf(flat == Vector3.Zero ? Vector3.UnitZ : flat);
        npc.Pose = "move";
        // Alternate swipes: the active hand reaches for the target during the second half of each interval.
        int interval = Math.Max(20, enraged ? _p.SwipeInterval / 2 : _p.SwipeInterval);
        int cycle = npc.Timer / interval;
        float t = (npc.Timer % interval) / (float)interval;
        float reach = t < 0.5f ? 0f : MathF.Sin((t - 0.5f) * 2f * MathF.PI);
        PlaceHands(npc, target.Center, 1f, cycle % 2, reach);
        if (npc.Timer >= _p.SpinInterval)
        {
            npc.Phase = 1;
            npc.Timer = 0;
        }
    }

    /// <summary>Hands rest either side of the skull; the swiping hand (index) is pulled towards the target.</summary>
    private void PlaceHands(Npc npc, Vector3 target, float spread, int swiping, float reach)
    {
        if (npc.Segments.Count < 2) return;
        var facing = new Vector3(MathF.Sin(npc.Yaw), 0, MathF.Cos(npc.Yaw));
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, facing));
        for (int i = 0; i < 2; i++)
        {
            var rest = npc.Center + side * (i == 0 ? 1f : -1f) * _p.HandSpacing * spread + new Vector3(0, -1.2f, 0) + facing * 0.6f;
            var goal = i == swiping ? Vector3.Lerp(rest, target, reach) : rest;
            npc.Segments[i] = Vector3.Lerp(npc.Segments[i], goal, 0.25f);
        }
    }
}

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
