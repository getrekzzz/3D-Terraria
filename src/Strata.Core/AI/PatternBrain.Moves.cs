using System.Numerics;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>The moves of a pattern boss (hover, chase, orbit, dash, teleport, hop, walk, slam, wait).</summary>
public sealed partial class PatternBrain
{
    /// <summary>Wind-up ticks, scaled in Expert (an unscaled telegraph is used exactly as written).</summary>
    private static int Telegraph(PatternMove m, float scale) => scale == 1f ? m.Telegraph : Math.Max(1, (int)MathF.Round(m.Telegraph * scale));

    /// <summary>
    /// Advances the current move; returns true while the boss is mid-teleport (untouchable). <paramref name="ground"/>
    /// is whether the boss walks under gravity in this phase (a ground boss in a fly phase steers in 3D).
    /// </summary>
    private static bool Move(Npc npc, PatternMove m, float speedScale, float telegraphScale, bool ground, ICombatTarget target, PatternExpert ex, BrainContext ctx)
    {
        float speed = m.Speed * speedScale;
        int telegraph = Telegraph(m, telegraphScale);
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
                if (npc.Timer <= telegraph)
                {
                    npc.Velocity *= 0.85f;
                    npc.Pose = "telegraph";
                    if (npc.Timer == telegraph)
                    {
                        npc.Direction = ground ? flat : (toTarget.LengthSquared() > 1e-4f ? Vector3.Normalize(toTarget) : Vector3.UnitX);
                        npc.Velocity = npc.Direction * speed + (ground ? new Vector3(0, npc.Velocity.Y, 0) : Vector3.Zero);
                    }
                }
                else
                {
                    npc.Pose = "attack";
                    var v = npc.Direction * speed;
                    npc.Velocity = ground ? new Vector3(v.X, npc.Velocity.Y, v.Z) : v;
                    if (npc.Timer >= telegraph + m.Ticks)
                    {
                        npc.Timer = 0;
                        done = ++npc.Counter2 >= m.Count;
                    }
                }
                break;
            case "teleport":
                if (npc.Timer < telegraph)
                {
                    npc.Velocity *= 0.8f;
                    npc.Pose = "teleport";
                    teleporting = true;
                }
                else if (npc.Timer == telegraph)
                {
                    float a = ctx.Rng.NextFloat() * MathF.Tau;
                    npc.Position = target.Center + new Vector3(MathF.Cos(a) * m.Distance, m.Height - npc.HalfExtents.Y, MathF.Sin(a) * m.Distance);
                    npc.Velocity = Vector3.Zero;
                }
                else npc.Velocity *= 0.9f;
                done = npc.Timer >= telegraph + m.Ticks;
                break;
            case "hop":
                if (!ground || npc.OnGround)
                {
                    npc.Velocity.X *= 0.6f;
                    npc.Velocity.Z *= 0.6f;
                    if (npc.Timer >= telegraph)
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
            case "slam":
                done = Slam(npc, m, speed, telegraph, target, ex, ctx);
                break;
            default: // wait
                npc.Velocity = ground ? new Vector3(npc.Velocity.X * 0.7f, npc.Velocity.Y, npc.Velocity.Z * 0.7f) : npc.Velocity * 0.85f;
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

    /// <summary>
    /// Rises to <see cref="PatternMove.Height"/> above the target and tracks it through the wind-up, then drops at
    /// Speed. Landing (on terrain, or at the target's feet for a boss that ignores terrain) raises an impact and fires
    /// the move's volley once; true when the slam is over (landed, or the fall ran out of ticks).
    /// </summary>
    private static bool Slam(Npc npc, PatternMove m, float speed, int telegraph, ICombatTarget target, PatternExpert ex, BrainContext ctx)
    {
        if (npc.Timer <= telegraph)
        {
            npc.Pose = "telegraph";
            Steer(npc, target.Center + new Vector3(0, m.Height, 0), MathF.Max(speed, 8f), 0.2f);
            return false;
        }
        npc.Pose = "attack";
        var flatOff = target.Center - npc.Center;
        npc.Velocity = new Vector3(Math.Clamp(flatOff.X * 3f, -speed, speed), -speed, Math.Clamp(flatOff.Z * 3f, -speed, speed));
        bool landed = npc.Def.CollideTerrain ? npc.OnGround && npc.Timer > telegraph + 1 : npc.Position.Y <= target.Position.Y;
        if (landed)
        {
            npc.Velocity = Vector3.Zero;
            ctx.Impact?.Invoke(npc.Position, 0.5f);
            if (m.Volley is { } v) Fire(npc, v, VolleyCount(v, ex, ctx.Expert), target, ctx);
            return true;
        }
        return npc.Timer >= telegraph + m.Ticks;
    }

    private static void Steer(Npc npc, Vector3 goal, float speed, float blend = 0.1f)
    {
        var d = goal - npc.Center;
        float len = d.Length();
        var want = len > 0.05f ? d / len * MathF.Min(speed, len * 3f) : Vector3.Zero;
        npc.Velocity = Vector3.Lerp(npc.Velocity, want, blend);
    }
}
