using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Core.AI;

/// <summary>
/// Runs a <see cref="PatternParams"/> boss: picks the phase for its remaining life (the Expert threshold in Expert),
/// loops that phase's moves (Expert-only moves only in Expert) and fires its volleys and summons, pulses its aura,
/// keeps to its leash and places its illusions. State: <see cref="Npc.Stage"/> = phase, Counter = move index, Timer =
/// ticks into the move, Counter2 = dashes/hops done, Direction = the current dash heading, Phase = 1 while gravity is
/// suspended (a fly phase or a slam), <see cref="Npc.PhaseTimer"/> = ticks into the phase (aura clock).
/// </summary>
public sealed partial class PatternBrain : IBrain
{
    private static readonly PatternExpert Classic = new();

    private readonly PatternParams _p;

    /// <summary>Per phase: the moves played in Classic (no Expert-only moves) and in Expert (all of them).</summary>
    private readonly PatternMove[][] _classic, _expert;

    /// <summary>Per phase: the guardian ids of <see cref="PatternPhase.ShieldedBy"/> and the summon pool.</summary>
    private readonly string[][] _guards, _pools;

    public PatternBrain(PatternParams p)
    {
        _p = p;
        int n = p.Phases.Length;
        _classic = new PatternMove[n][];
        _expert = new PatternMove[n][];
        _guards = new string[n][];
        _pools = new string[n][];
        for (int i = 0; i < n; i++)
        {
            var ph = p.Phases[i];
            _expert[i] = ph.Moves;
            var classic = Array.FindAll(ph.Moves, m => !m.ExpertOnly);
            _classic[i] = classic.Length > 0 ? classic : ph.Moves;
            _guards[i] = (ph.ShieldedBy ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            _pools[i] = ph.Summon is { } s ? (s.Pool.Length > 0 ? s.Pool : new[] { s.Enemy }) : Array.Empty<string>();
        }
    }

    public bool UsesGravity => _p.Ground;

    public bool UsesGravityFor(Npc npc) => _p.Ground && npc.Phase == 0;

    public void Think(Npc npc, BrainContext ctx)
    {
        float life = npc.Life / (float)Math.Max(1, npc.MaxLife);
        bool expert = ctx.Expert;
        int stage = 0;
        for (int i = 0; i < _p.Phases.Length; i++)
        {
            var ph = _p.Phases[i];
            if (life <= (expert && ph.ExpertBelow is { } eb ? eb : ph.Below)) stage = i;
        }
        // A guarded phase ends once its guardians (summoned on entering it) have all fallen: the Brain is exposed at full life.
        if (_p.Phases[stage].UntilGuardsFall && stage + 1 < _p.Phases.Length && npc.Stage >= stage && !Guarded(_guards[stage], ctx)) stage++;
        var phase = _p.Phases[stage];
        var ex = expert ? phase.Expert ?? Classic : Classic;
        if (stage != npc.Stage) Enter(npc, stage, phase, ex, ctx);
        npc.PhaseTimer++;
        npc.DamageScale = phase.Damage * ex.Damage;
        if (phase.Defense is { } defense) npc.DefenseOverride = defense;
        bool shielded = Guarded(_guards[stage], ctx);
        Summon(npc, phase, _pools[stage], ex, ctx);
        var target = ctx.Target;
        if (target == null)
        {
            npc.Phase = _p.Ground && phase.Fly ? 1 : 0;
            npc.Velocity *= 0.9f;
            npc.Pose = "idle";
            npc.Invulnerable = shielded;
            npc.Illusions = 0; // illusions are mirrored around a target: none without one
            return;
        }
        npc.Illusions = phase.Illusions;
        npc.TargetPlayer = 0;
        var moves = expert ? _expert[stage] : _classic[stage];
        var move = moves[npc.Counter % moves.Length];
        bool ground = _p.Ground && !phase.Fly;
        npc.Phase = _p.Ground && (phase.Fly || move.Type == "slam") ? 1 : 0;
        bool teleporting = Move(npc, move, phase.Speed * ex.Speed, ex.TelegraphScale, ground, target, ex, ctx);
        if (npc.Leader != null) npc.Velocity = (npc.Anchor - npc.Center) * 8f; // a fixed part keeps to its place on the body
        if (phase.Leash is { } leash) Leash(npc, leash);
        npc.Invulnerable = shielded || teleporting;
        if (phase.Volley is { } pv) Fire(npc, pv, ref npc.VolleyTimer, VolleyInterval(pv, ex, expert), VolleyCount(pv, ex, expert), target, ctx);
        if (move.Volley is { } mv && move.Type != "slam")
        {
            int t = npc.Timer, interval = VolleyInterval(mv, ex, expert); // move volleys follow the move's own clock
            if (t % interval == interval / 2) Fire(npc, mv, VolleyCount(mv, ex, expert), target, ctx);
        }
        if (phase.Aura is { } aura && (!aura.ExpertOnly || expert) && npc.PhaseTimer % aura.Interval == 0)
            ctx.Pulse?.Invoke(npc.Center, aura.Radius, aura.Buff, aura.Ticks);
        if (npc.Illusions > 0) npc.MirrorAround = target.Center;
    }

    private static int VolleyInterval(PatternVolley v, PatternExpert ex, bool expert) =>
        Math.Max(1, !expert ? v.Interval : v.ExpertInterval ?? (ex.VolleyInterval == 1f ? v.Interval : (int)MathF.Round(v.Interval * ex.VolleyInterval)));

    private static int VolleyCount(PatternVolley v, PatternExpert ex, bool expert) => !expert ? v.Count : v.ExpertCount ?? v.Count + ex.VolleyCount;

    private static void Enter(Npc npc, int stage, PatternPhase phase, PatternExpert ex, BrainContext ctx)
    {
        bool first = npc.Stage < 0;
        npc.Stage = stage;
        npc.Counter = 0;
        npc.Counter2 = 0;
        npc.Timer = 0;
        npc.SummonTimer = 0;
        npc.PhaseTimer = 0;
        npc.ModelOverride = phase.Model;
        npc.Enraged = stage > 0;
        if (!first && !string.IsNullOrEmpty(phase.Message)) ctx.Announce?.Invoke(phase.Message);
        if (phase.Summon is not { Once: true } once || ctx.Spawn == null || !ctx.Content.Enemies.TryGet(once.Enemy, out var def)) return;
        int count = once.Max + ex.SummonMax;
        var offset = OffsetOf(once);
        for (int i = 0; i < count; i++)
        {
            if (!once.Tether)
            {
                ctx.Spawn(def, npc.Center + offset + Scatter(ref ctx.Rng, 3f));
                continue;
            }
            var o = Ring(i, count, once.Radius) + offset;
            if (ctx.Spawn(def, npc.Center + o) is { } m) NpcSystem.Tether(m, npc, o);
        }
    }

    private static Vector3 Scatter(ref Determinism.Pcg32 rng, float r) => new(rng.NextSigned() * r, rng.NextSigned() * r * 0.5f, rng.NextSigned() * r);

    /// <summary>Slot <paramref name="i"/> of <paramref name="n"/> evenly spaced on a flat ring.</summary>
    private static Vector3 Ring(int i, int n, float radius)
    {
        float a = i * MathF.Tau / Math.Max(1, n);
        return new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
    }

    private static Vector3 OffsetOf(PatternSummon s) =>
        s.Offset.Length == 0 ? Vector3.Zero : new Vector3(s.Offset[0], s.Offset.Length > 1 ? s.Offset[1] : 0, s.Offset.Length > 2 ? s.Offset[2] : 0);

    /// <summary>Any guardian of the phase's kinds lives.</summary>
    private static bool Guarded(string[] kinds, BrainContext ctx)
    {
        if (kinds.Length == 0 || ctx.CountAlive == null) return false;
        foreach (var id in kinds)
            if (ctx.CountAlive(id) > 0) return true;
        return false;
    }

    private static void Summon(Npc npc, PatternPhase phase, string[] pool, PatternExpert ex, BrainContext ctx)
    {
        if (phase.Summon is not { Once: false } s || ctx.Spawn == null) return;
        if (++npc.SummonTimer < s.Interval) return;
        npc.SummonTimer = 0;
        if (s.Range > 0 && (ctx.Target == null || ctx.TargetDistance > s.Range)) return;
        int alive = 0, max = s.Max + ex.SummonMax;
        foreach (var id in pool) alive += ctx.CountAlive?.Invoke(id) ?? 0;
        var offset = OffsetOf(s);
        for (int i = 0; i < s.Count && alive + i < max; i++)
        {
            if (!ctx.Content.Enemies.TryGet(pool.Length == 1 ? pool[0] : pool[ctx.Rng.NextInt(pool.Length)], out var def)) continue;
            if (s.Tether)
            {
                var o = Ring(alive + i, max, s.Radius) + offset;
                if (ctx.Spawn(def, npc.Center + o) is { } m) NpcSystem.Tether(m, npc, o);
                continue;
            }
            if (s.Radius <= 3f)
            {
                ctx.Spawn(def, npc.Center + offset + Scatter(ref ctx.Rng, s.Radius));
                continue;
            }
            float a = ctx.Rng.NextFloat() * MathF.Tau, d = s.Radius * ctx.Rng.Range(0.4f, 1f);
            var at = npc.Center + offset + new Vector3(MathF.Cos(a) * d, 0, MathF.Sin(a) * d);
            if (def.Gravity)
            {
                // Walkers stand on the ground there (none spawns inside a hill or in the air).
                if (ctx.Ground?.Invoke(at) is not { } feet) continue;
                at = feet + new Vector3(0, def.Height * 0.25f * def.Scale, 0);
            }
            ctx.Spawn(def, at);
        }
    }

    /// <summary>
    /// Beyond the leash radius from its living anchors' centroid, the boss is pulled back to the edge and loses the
    /// outward part of its velocity (inside it moves freely, so dashes are not fought).
    /// </summary>
    private static void Leash(Npc npc, PatternLeash leash)
    {
        var sum = Vector3.Zero;
        int n = 0;
        foreach (var f in npc.Followers)
            if (f.Alive && !f.Removed && f.Def.Id == leash.Kind)
            {
                sum += f.Center;
                n++;
            }
        if (n == 0) return;
        var centroid = sum / n;
        var off = npc.Center - centroid;
        float len = off.Length();
        if (len <= leash.Radius || len < 1e-4f) return;
        var dir = off / len;
        npc.Position -= dir * (len - leash.Radius);
        float outward = Vector3.Dot(npc.Velocity, dir);
        if (outward > 0) npc.Velocity -= dir * (outward * 1.5f); // a little spring back
    }

    private static void Fire(Npc npc, PatternVolley v, ref int timer, int interval, int count, ICombatTarget target, BrainContext ctx)
    {
        if (++timer < interval) return;
        timer = 0;
        Fire(npc, v, count, target, ctx);
    }

    private static void Fire(Npc npc, PatternVolley v, int count, ICombatTarget target, BrainContext ctx)
    {
        if (ctx.Shoot == null || count <= 0 || Vector3.Distance(npc.Center, target.Center) > v.Range) return;
        var from = npc.Center;
        var aim = target.Center - from;
        var dir = aim.LengthSquared() > 1e-4f ? Vector3.Normalize(aim) : Vector3.UnitX;
        float spread = v.Spread * MathF.PI / 180f;
        for (int i = 0; i < count; i++)
        {
            Vector3 d;
            if (v.Ring)
            {
                float a = i * MathF.Tau / count;
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
