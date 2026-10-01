using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// A boss's free hand (Skeletron's): it rests at its place on the body (its <see cref="Npc.Anchor"/>, kept on the
/// leader's turning offset), and every <see cref="HandParams.AttackInterval"/> ticks winds up beside the target and
/// strikes through where the target stood — a slap dropping from above, or a sideways swipe — then flies back. It holds
/// its rest while its leader spins, flies through walls, and takes its leader's rage (damage, defense and speed).
/// Phase: 0 rest, 1 wind-up, 2 strike, 3 recover. Counter = attacks made; Direction = strike velocity.
/// </summary>
public sealed class HandBrain : IBrain
{
    /// <summary>A strike carries on this far (m) past the point it aimed at.</summary>
    private const float Overshoot = 3f;

    private const int MaxRecoverTicks = 120;

    private readonly HandParams _p;

    public HandBrain(HandParams p) => _p = p;

    public bool UsesGravity => false;
    public bool MovesFreely => true;

    public void Think(Npc npc, BrainContext ctx)
    {
        var leader = npc.Leader;
        if (leader != null)
        {
            npc.DamageScale = leader.DamageScale;
            npc.DefenseOverride = leader.DefenseOverride;
            npc.SpeedScale = leader.SpeedScale;
            npc.Frenzied = leader.Frenzied;
            npc.Yaw = leader.Yaw;
        }
        float side = npc.LeaderOffset.X < 0 ? -1f : 1f;
        float speedMul = ctx.Expert ? _p.ExpertSpeed : 1f;
        int interval = Math.Max(1, (int)(_p.AttackInterval * (ctx.Expert ? _p.ExpertInterval : 1f)));
        var target = ctx.Target;
        bool leaderSpins = leader is { Phase: 1 } && leader.Def.Brain.Type == BrainSpecs.CryptWarden;
        if (target == null && npc.Phase is 1 or 2) Begin(npc, 3);
        npc.Timer++;
        switch (npc.Phase)
        {
            case 1:
            {
                var goal = WindupSpot(npc, target!.Center, side, leader);
                var to = goal - npc.Center;
                float d = to.Length();
                float cap = _p.StrikeSpeed * 1.5f * speedMul;
                npc.Velocity = d > 1e-3f ? to / d * MathF.Min(cap, d * 6f) : Vector3.Zero;
                npc.Pose = "telegraph";
                if (npc.Timer < _p.WindupTicks) return;
                // Strike through where the target stands now, a little past it.
                var aim = target.Center - npc.Center;
                float len = aim.Length();
                var dir = len > 1e-3f ? aim / len : -Vector3.UnitY;
                float strike = _p.StrikeSpeed * speedMul * (Slaps(npc, side) ? 1f : 1.1f);
                npc.Direction = dir * strike;
                npc.Counter2 = Math.Clamp((int)MathF.Ceiling((len + Overshoot) / strike * Units.TicksPerSecond), 1, _p.StrikeTicks);
                Begin(npc, 2);
                ctx.Cue?.Invoke(npc, "hand_slap");
                return;
            }
            case 2:
                npc.Velocity = npc.Direction;
                npc.Pose = "attack";
                if (npc.Timer >= npc.Counter2) Begin(npc, 3);
                return;
            case 3:
            {
                var to = npc.Anchor - npc.Center;
                float d = to.Length();
                // At least the return speed, faster when far behind a moving body, never past the anchor in one tick.
                float speed = MathF.Min(MathF.Max(_p.ReturnSpeed * speedMul, d * 4f), d * Units.TicksPerSecond);
                npc.Velocity = d > 1e-3f ? to / d * speed : Vector3.Zero;
                npc.Pose = "move";
                if (d < 0.6f || npc.Timer >= MaxRecoverTicks)
                {
                    npc.Counter++;
                    Begin(npc, 0);
                }
                return;
            }
        }
        // At rest on the body, bobbing gently; the clock runs only while there is something to strike and the body is
        // not spinning. The right hand's first attack waits half an interval, so a pair takes turns.
        var rest = npc.Anchor + new Vector3(0, MathF.Sin((npc.Age + side * 40f) * 0.05f) * 0.25f, 0);
        npc.Velocity = (rest - npc.Center) * 8f;
        npc.Pose = "idle";
        if (target == null || leaderSpins)
        {
            npc.Timer--;
            return;
        }
        int wait = interval + (npc.Counter == 0 && side > 0 ? interval / 2 : 0);
        if (npc.Timer >= wait) Begin(npc, 1);
    }

    private static void Begin(Npc npc, int phase)
    {
        npc.Phase = phase;
        npc.Timer = 0;
    }

    /// <summary>Which attack comes next: alternating hands start on different ones, so a pair slaps and swipes in turn.</summary>
    private bool Slaps(Npc npc, float side) => _p.Style switch
    {
        HandParams.Slap => true,
        HandParams.Swipe => false,
        _ => (npc.Counter + (side > 0 ? 1 : 0)) % 2 == 0,
    };

    /// <summary>A slap winds up high over the target on the hand's side; a swipe out to that side, just above it.</summary>
    private Vector3 WindupSpot(Npc npc, Vector3 target, float side, Npc? leader)
    {
        float yaw = leader?.Yaw ?? npc.Yaw;
        var across = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw)) * side; // the leader's +X turned with it
        return Slaps(npc, side) ? target + new Vector3(0, 4f, 0) + across * 1.5f : target + across * 5f + new Vector3(0, 1f, 0);
    }
}
