using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// Gel Sovereign (King Slime): charges up big hops towards the target, slams down (an impact), sheds minion gels as it
/// loses life and shrinks with it (model and box), and teleports beside a target that runs too far or stays out of
/// sight (shrinking first as a telegraph, invulnerable meanwhile). Expert: every third minion is a Spiked Slime and the
/// teleport winds up faster. Phase: 0 = hopping, 1 = teleport shrink, 2 = reappear; PhaseTimer = ticks without sight.
/// </summary>
public sealed class GelSovereignBrain : IBrain
{
    private readonly GelSovereignParams _p;

    public GelSovereignBrain(GelSovereignParams p) => _p = p;

    public bool UsesGravity => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        Shrink(npc);
        ShedMinions(npc, ctx);
        if (npc.LandingSpeed > _p.ImpactSpeed)
        {
            ctx.Impact?.Invoke(npc.Position, 0.3f);
            ctx.Cue?.Invoke(npc, "land");
        }
        if (ctx.Target == null) return;
        npc.TargetPlayer = 0;
        var head = npc.Center + new Vector3(0, npc.HalfExtents.Y * 0.8f, 0);
        npc.PhaseTimer = ctx.InSight?.Invoke(head, ctx.Target.Eye) ?? true ? 0 : npc.PhaseTimer + 1;
        if (npc.Phase == 1)
        {
            npc.Pose = "teleport";
            npc.Invulnerable = true;
            npc.Velocity = new Vector3(0, npc.Velocity.Y, 0);
            if (++npc.Timer < (ctx.Expert ? _p.ExpertTeleportTicks : _p.TeleportTicks)) return;
            // Reappear beside the target, or right above it when that spot is still behind a wall.
            var dir = BrainFactory.Flat(npc.Position - ctx.Target.Position);
            if (dir == Vector3.Zero) dir = Vector3.UnitX;
            if (TeleportSpot(npc, ctx, dir) is { } spot) npc.Position = spot; // nowhere free: it reappears where it was
            npc.Velocity = Vector3.Zero;
            npc.Phase = 2;
            npc.Timer = 0;
            npc.PhaseTimer = 0;
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
        if ((ctx.TargetDistance > _p.TeleportDistance || npc.PhaseTimer > _p.LostSightTicks) && npc.OnGround)
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

    /// <summary>
    /// Where the teleport lands (feet): beside the target when that spot sees it, else right above it, else on the ground
    /// around it; never a spot whose box is in terrain (it would be stuck there). Null when nothing near is free.
    /// </summary>
    private static Vector3? TeleportSpot(Npc npc, BrainContext ctx, Vector3 dir)
    {
        var target = ctx.Target!;
        var up = new Vector3(0, npc.HalfExtents.Y, 0);
        var beside = target.Position + dir * 5f + new Vector3(0, 3f, 0);
        if (Free(beside) && (ctx.InSight?.Invoke(beside + up, target.Eye) ?? true)) return beside;
        var above = target.Position + new Vector3(0, 2.5f, 0);
        if (Free(above)) return above;
        if (Free(target.Position + new Vector3(0, 0.05f, 0))) return target.Position + new Vector3(0, 0.05f, 0);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4f;
            var around = target.Position + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 4f;
            if (ctx.Ground?.Invoke(around) is { } feet && Free(feet + new Vector3(0, 0.05f, 0))
                && (ctx.InSight?.Invoke(feet + up, target.Eye) ?? true)) return feet + new Vector3(0, 0.05f, 0);
        }
        return null;

        bool Free(Vector3 feet) => ctx.Fits?.Invoke(feet + up, npc.HalfExtents) ?? true;
    }

    /// <summary>Model and box scale with the life left (Terraria's King Slime goes from 1.25 to 0.75).</summary>
    private void Shrink(Npc npc)
    {
        float life = Math.Clamp(npc.Life / (float)Math.Max(1, npc.MaxLife), 0f, 1f);
        float scale = _p.EmptyScale + (_p.FullScale - _p.EmptyScale) * life;
        if (MathF.Abs(scale - npc.VisualScale) < 0.005f) return;
        npc.VisualScale = scale;
        npc.HalfExtents = npc.BaseHalfExtents * scale; // the feet stay put, so a shrinking box never sinks into terrain
    }

    private void ShedMinions(Npc npc, BrainContext ctx)
    {
        if (_p.Minion == null || ctx.Spawn == null || !ctx.Content.Enemies.TryGet(_p.Minion, out var minion)) return;
        float lost = 1f - npc.Life / (float)npc.MaxLife;
        int due = (int)(lost / _p.MinionEveryLife);
        while (npc.Counter2 < due)
        {
            npc.Counter2++;
            var kind = minion;
            if (ctx.Expert && npc.Counter2 % 3 == 0 && _p.ExpertMinion != null && ctx.Content.Enemies.TryGet(_p.ExpertMinion, out var spiked)) kind = spiked;
            var offset = new Vector3(ctx.Rng.NextSigned(), 0.5f, ctx.Rng.NextSigned()) * 1.5f;
            if (ctx.Spawn(kind, npc.Center + offset) is { } m) m.Velocity = new Vector3(offset.X * 3f, 6f, offset.Z * 3f);
        }
    }
}

/// <summary>
/// The Watcher (Eye of Cthulhu, the Twins): phase 1 orbits the target summoning servant eyes, then telegraphs (stares)
/// and dashes in bursts. Below a life fraction it transforms (spinning in place, untouchable, into its phase 2 model),
/// then fights with its phase 2 defense and damage, dashing faster and more often; in Expert it chains more dashes as it
/// weakens. Phase: 0 = orbit, 1 = telegraph, 2 = dash; <see cref="Npc.Counter"/> counts dashes in the burst;
/// <see cref="Npc.Stage"/>: 0 = phase 1, 1 = transforming (Counter2 = ticks left), 2 = phase 2.
/// </summary>
public sealed class WatcherBrain : IBrain
{
    private readonly WatcherParams _p;

    public WatcherBrain(WatcherParams p) => _p = p;

    public bool UsesGravity => false;

    public void Think(Npc npc, BrainContext ctx)
    {
        float life = npc.Life / (float)Math.Max(1, npc.MaxLife);
        if (npc.Stage < 0) npc.Stage = 0;
        if (npc.Stage == 0 && life <= _p.Phase2Life)
        {
            npc.Stage = 1;
            npc.Counter2 = _p.TransformTicks;
            npc.Phase = 0;
            npc.Timer = 0;
            npc.Counter = 0;
            if (_p.Phase2Model != null) npc.ModelOverride = _p.Phase2Model;
            if (_p.TransformTicks > 0) ctx.Cue?.Invoke(npc, "boss_roar");
        }
        if (npc.Stage == 1)
        {
            if (npc.Counter2 > 0)
            {
                // Tearing open: it spins in place and cannot be hurt.
                npc.Counter2--;
                npc.Velocity *= 0.9f;
                npc.Yaw += 0.45f;
                npc.Pose = "transform";
                npc.Invulnerable = true;
                return;
            }
            npc.Invulnerable = false;
            npc.Stage = 2;
        }
        bool enraged = npc.Stage == 2;
        npc.Enraged = enraged;
        if (enraged)
        {
            if (npc.ModelOverride == null && _p.Phase2Model != null) npc.ModelOverride = _p.Phase2Model;
            if (_p.Phase2Defense >= 0) npc.DefenseOverride = _p.Phase2Defense;
            npc.DamageScale = _p.Phase2Damage;
        }
        if (ctx.Target == null) return;
        npc.TargetPlayer = 0;
        int dashes = _p.DashesPerBurst + (enraged ? _p.Phase2ExtraDashes : 0);
        int telegraph = enraged ? _p.TelegraphTicks * 2 / 3 : _p.TelegraphTicks;
        if (enraged && ctx.Expert && _p.ExpertMaxDashes > 0)
        {
            dashes = Math.Max(dashes, Math.Min(_p.ExpertMaxDashes, _p.DashesPerBurst + (int)((_p.Phase2Life - life) / 0.1f + 1e-3f)));
            telegraph = Math.Max(1, (int)MathF.Round(telegraph * _p.ExpertTelegraphScale));
        }
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
                if (npc.Timer >= telegraph)
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
                    npc.Phase = npc.Counter >= dashes ? 0 : 1;
                }
                break;
        }
        if (toTarget.LengthSquared() > 1e-4f) npc.Yaw = BrainFactory.YawOf(toTarget);
    }
}
