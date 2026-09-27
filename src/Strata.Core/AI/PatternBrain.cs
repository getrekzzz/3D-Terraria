using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Core.AI;

/// <summary>
/// Runs a <see cref="PatternParams"/> boss: picks the phase for its remaining life, loops that phase's moves and
/// fires its volleys and summons. State: <see cref="Npc.Stage"/> = phase, Counter = move index, Timer = ticks into
/// the move, Counter2 = dashes/hops done, Direction = the current dash heading.
/// </summary>
public sealed class PatternBrain : IBrain
{
    private readonly PatternParams _p;

    public PatternBrain(PatternParams p) => _p = p;

    public bool UsesGravity => _p.Ground;

    public void Think(Npc npc, BrainContext ctx)
    {
        float life = npc.Life / (float)Math.Max(1, npc.MaxLife);
        int stage = 0;
        for (int i = 0; i < _p.Phases.Length; i++)
            if (life <= _p.Phases[i].Below) stage = i;
        var phase = _p.Phases[stage];
        if (stage != npc.Stage) Enter(npc, stage, phase, ctx);
        npc.DamageScale = phase.Damage;
        bool shielded = phase.ShieldedBy != null && (ctx.CountAlive?.Invoke(phase.ShieldedBy) ?? 0) > 0;
        Summon(npc, phase, ctx);
        var target = ctx.Target;
        if (target == null)
        {
            npc.Velocity *= 0.9f;
            npc.Pose = "idle";
            npc.Invulnerable = shielded;
            return;
        }
        npc.TargetPlayer = 0;
        var move = phase.Moves[npc.Counter % phase.Moves.Length];
        bool teleporting = Move(npc, move, phase.Speed, target, ctx);
        npc.Invulnerable = shielded || teleporting;
        if (phase.Volley != null) Fire(npc, phase.Volley, ref npc.VolleyTimer, target, ctx);
        if (move.Volley != null)
        {
            int t = npc.Timer; // move volleys follow the move's own clock
            if (t % Math.Max(1, move.Volley.Interval) == move.Volley.Interval / 2) Fire(npc, move.Volley, target, ctx);
        }
    }

    private static void Enter(Npc npc, int stage, PatternPhase phase, BrainContext ctx)
    {
        bool first = npc.Stage < 0;
        npc.Stage = stage;
        npc.Counter = 0;
        npc.Counter2 = 0;
        npc.Timer = 0;
        npc.SummonTimer = 0;
        npc.ModelOverride = phase.Model;
        npc.Enraged = stage > 0;
        if (!first && !string.IsNullOrEmpty(phase.Message)) ctx.Announce?.Invoke(phase.Message);
        if (phase.Summon is { Once: true } once && ctx.Content.Enemies.TryGet(once.Enemy, out var def))
            for (int i = 0; i < once.Max; i++) ctx.Spawn?.Invoke(def, npc.Center + Scatter(ref ctx.Rng, 3f));
    }

    private static Vector3 Scatter(ref Determinism.Pcg32 rng, float r) => new(rng.NextSigned() * r, rng.NextSigned() * r * 0.5f, rng.NextSigned() * r);

    private static void Summon(Npc npc, PatternPhase phase, BrainContext ctx)
    {
        if (phase.Summon is not { Once: false } s || ctx.Spawn == null || !ctx.Content.Enemies.TryGet(s.Enemy, out var def)) return;
        if (++npc.SummonTimer < s.Interval) return;
        npc.SummonTimer = 0;
        int alive = ctx.CountAlive?.Invoke(s.Enemy) ?? 0;
        for (int i = 0; i < s.Count && alive + i < s.Max; i++) ctx.Spawn(def, npc.Center + Scatter(ref ctx.Rng, 2f));
    }

    /// <summary>Advances the current move; returns true while the boss is mid-teleport (untouchable).</summary>
    private bool Move(Npc npc, PatternMove m, float speedScale, PlayerEntity target, BrainContext ctx)
    {
        float speed = m.Speed * speedScale;
        var toTarget = target.Center - npc.Center;
        var flat = BrainFactory.Flat(toTarget);
        if (flat != Vector3.Zero) npc.Yaw = BrainFactory.YawOf(flat);
        npc.Timer++;
        npc.Pose = m.Pose ?? "move";
        bool done = false, teleporting = false;
        switch (m.Type)
        {
            case "hover":
            {
                var side = BrainFactory.Flat(npc.Center - target.Center);
                if (side == Vector3.Zero) side = Vector3.UnitX;
                Steer(npc, target.Center + side * m.Distance + new Vector3(0, m.Height, 0), speed);
                done = npc.Timer >= m.Ticks;
                break;
            }
            case "chase":
                Steer(npc, target.Center + new Vector3(0, m.Height * 0.2f, 0), speed);
                done = npc.Timer >= m.Ticks;
                break;
            case "orbit":
            {
                var off = npc.Center - target.Center;
                float angle = MathF.Atan2(off.Z, off.X) + speed / MathF.Max(1f, m.Distance) / Common.Units.TicksPerSecond;
                Steer(npc, target.Center + new Vector3(MathF.Cos(angle) * m.Distance, m.Height, MathF.Sin(angle) * m.Distance), speed * 1.6f);
                done = npc.Timer >= m.Ticks;
                break;
            }
            case "dash":
                if (npc.Timer <= m.Telegraph)
                {
                    npc.Velocity *= 0.85f;
                    npc.Pose = "telegraph";
                    if (npc.Timer == m.Telegraph)
                    {
                        npc.Direction = _p.Ground ? flat : (toTarget.LengthSquared() > 1e-4f ? Vector3.Normalize(toTarget) : Vector3.UnitX);
                        npc.Velocity = npc.Direction * speed + (_p.Ground ? new Vector3(0, npc.Velocity.Y, 0) : Vector3.Zero);
                    }
                }
                else
                {
                    npc.Pose = "attack";
                    var v = npc.Direction * speed;
                    npc.Velocity = _p.Ground ? new Vector3(v.X, npc.Velocity.Y, v.Z) : v;
                    if (npc.Timer >= m.Telegraph + m.Ticks)
                    {
                        npc.Timer = 0;
                        done = ++npc.Counter2 >= m.Count;
                    }
                }
                break;
            case "teleport":
                if (npc.Timer < m.Telegraph)
                {
                    npc.Velocity *= 0.8f;
                    npc.Pose = "teleport";
                    teleporting = true;
                }
                else if (npc.Timer == m.Telegraph)
                {
                    float a = ctx.Rng.NextFloat() * MathF.Tau;
                    npc.Position = target.Center + new Vector3(MathF.Cos(a) * m.Distance, m.Height - npc.HalfExtents.Y, MathF.Sin(a) * m.Distance);
                    npc.Velocity = Vector3.Zero;
                }
                else npc.Velocity *= 0.9f;
                done = npc.Timer >= m.Telegraph + m.Ticks;
                break;
            case "hop":
                if (!_p.Ground || npc.OnGround)
                {
                    npc.Velocity.X *= 0.6f;
                    npc.Velocity.Z *= 0.6f;
                    if (npc.Timer >= m.Telegraph)
                    {
                        npc.Velocity = flat * speed + new Vector3(0, BrainFactory.JumpVelocity(m.Height), 0);
                        npc.Timer = 0;
                        done = ++npc.Counter2 >= m.Count;
                    }
                    else npc.Pose = "telegraph";
                }
                else npc.Pose = "jump";
                break;
            case "walk":
            {
                var v = flat * speed;
                npc.Velocity = new Vector3(v.X, npc.Velocity.Y, v.Z);
                // Step up ledges now and then while pressing on.
                if (npc.OnGround && npc.Timer % 45 == 0 && new Vector2(toTarget.X, toTarget.Z).Length() > 3f) npc.Velocity.Y = BrainFactory.JumpVelocity(4f);
                done = npc.Timer >= m.Ticks;
                break;
            }
            default: // wait
                npc.Velocity = _p.Ground ? new Vector3(npc.Velocity.X * 0.7f, npc.Velocity.Y, npc.Velocity.Z * 0.7f) : npc.Velocity * 0.85f;
                npc.Pose = m.Pose ?? "idle";
                done = npc.Timer >= m.Ticks;
                break;
        }
        if (done)
        {
            npc.Counter++;
            npc.Counter2 = 0;
            npc.Timer = 0;
        }
        return teleporting;
    }

    private static void Steer(Npc npc, Vector3 goal, float speed)
    {
        var d = goal - npc.Center;
        float len = d.Length();
        var want = len > 0.05f ? d / len * MathF.Min(speed, len * 3f) : Vector3.Zero;
        npc.Velocity = Vector3.Lerp(npc.Velocity, want, 0.1f);
    }

    private static void Fire(Npc npc, PatternVolley v, ref int timer, PlayerEntity target, BrainContext ctx)
    {
        if (++timer < v.Interval) return;
        timer = 0;
        Fire(npc, v, target, ctx);
    }

    private static void Fire(Npc npc, PatternVolley v, PlayerEntity target, BrainContext ctx)
    {
        if (ctx.Shoot == null || Vector3.Distance(npc.Center, target.Center) > v.Range) return;
        var from = npc.Center;
        var aim = target.Center - from;
        var dir = aim.LengthSquared() > 1e-4f ? Vector3.Normalize(aim) : Vector3.UnitX;
        float spread = v.Spread * MathF.PI / 180f;
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 d;
            if (v.Ring)
            {
                float a = i * MathF.Tau / v.Count;
                d = Vector3.Normalize(new Vector3(MathF.Cos(a), dir.Y * 0.5f, MathF.Sin(a)));
            }
            else
            {
                var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY) + new Vector3(1e-4f, 0, 0));
                var up = Vector3.Cross(side, dir);
                d = spread > 0 ? Vector3.Normalize(dir + side * ctx.Rng.NextSigned() * spread + up * ctx.Rng.NextSigned() * spread * 0.5f) : dir;
            }
            ctx.Shoot(v.Projectile, from + d * (npc.HalfExtents.X + 0.3f), d * v.Speed, v.Damage);
        }
        npc.Pose = "attack";
    }
}
