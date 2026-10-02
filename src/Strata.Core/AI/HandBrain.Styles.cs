using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>Skeletron Prime's tools: the saw's jabs and the vice's creeping wind-up ("hold" needs no motion of its own).</summary>
public sealed partial class HandBrain
{
    /// <summary>
    /// "saw": no wind-up, a straight jab at the target that stops <see cref="HandParams.Reach"/> from the arm's place on
    /// the body (or just past the target when nearer), then the usual return.
    /// </summary>
    private void Jab(Npc npc, BrainContext ctx, Vector3 target, float speedMul)
    {
        var from = npc.Center;
        var aim = target - npc.Anchor;
        float len = aim.Length();
        var goal = len > _p.Reach ? npc.Anchor + aim / len * _p.Reach : target + (len > 1e-3f ? aim / len : -Vector3.UnitY) * 0.5f;
        var to = goal - from;
        float dist = to.Length();
        float speed = _p.StrikeSpeed * speedMul;
        npc.Direction = dist > 1e-3f ? to / dist * speed : Vector3.Zero;
        npc.Counter2 = Math.Clamp((int)MathF.Ceiling(dist / speed * Units.TicksPerSecond), 1, _p.StrikeTicks);
        Begin(npc, 2);
        ctx.Cue?.Invoke(npc, "hand_slap");
    }

    /// <summary>"vice": through its wind-up it creeps towards the target, stopping a few metres short, before the lunge.</summary>
    private static Vector3 Creep(Npc npc, Vector3 target)
    {
        var away = npc.Center - target;
        float d = away.Length();
        if (d < 1e-3f) return npc.Center;
        float keep = MathF.Max(3f, d - 2.5f); // a slow approach: the step below caps the speed
        return target + away / d * keep;
    }
}
