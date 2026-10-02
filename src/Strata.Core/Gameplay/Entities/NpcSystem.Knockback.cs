using System.Numerics;
using Strata.Core.AI;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class NpcSystem
{
    /// <summary>Knockback speed per point of (resisted) knockback, in m/s.</summary>
    public const float KnockbackSpeed = 2.0f;

    /// <summary>
    /// Pushes a creature away from a hit. Ordinary creatures also reel for a moment (their brain stops steering, so
    /// the push carries them away and a dash is broken off), as knockback works in the reference game; bosses only
    /// and their parts only take the push.
    /// </summary>
    private static void Knock(Npc npc, Vector3 away, float knockback)
    {
        if (knockback <= 0f) return;
        var push = away * knockback * KnockbackSpeed + new Vector3(0, knockback * 0.4f, 0);
        if (npc.IsBoss || npc.IsPart || knockback < 0.3f)
        {
            npc.Velocity += push;
            return;
        }
        // The push replaces the creature's own horizontal motion, so walking into the blow cannot cancel it.
        npc.Velocity = new Vector3(push.X, Math.Max(npc.Velocity.Y, 0f) + push.Y, push.Z);
        npc.StunTicks = Math.Max(npc.StunTicks, (int)(6 + knockback * 2f));
        npc.Phase = 0;
        npc.Timer = 0;
    }

    /// <summary>A reeling creature coasts on its knockback with friction instead of thinking.</summary>
    private static void Reel(Npc npc, IBrain brain)
    {
        npc.StunTicks--;
        bool falls = brain.UsesGravityFor(npc) && npc.Def.Gravity;
        float drag = !falls ? 0.93f : npc.OnGround ? 0.88f : 0.96f;
        npc.Velocity.X *= drag;
        npc.Velocity.Z *= drag;
        if (!falls) npc.Velocity.Y *= drag;
    }
}
