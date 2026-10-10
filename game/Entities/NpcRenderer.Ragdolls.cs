using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Human creatures crumple into physics ragdolls when they die (X-16): thrown along the killing blow's knockback, they
/// tumble for <see cref="RagdollTime"/> seconds, then sink through the ground. At most <see cref="RagdollBudget"/>
/// lie at once (the graphics preset's <c>ragdolls</c>); past it, and for scaled bodies, the death clip plays instead.
/// Creatures without a skeleton keep their gore (P3-20).
/// </summary>
public sealed partial class NpcRenderer
{
    private const float RagdollTime = 3f, DropTime = 0.8f;

    /// <summary>Ragdolls allowed at once (0 turns them off).</summary>
    public int RagdollBudget { get; set; } = 3;

    /// <summary>Ragdolls lying now (probe <c>fx.ragdolls</c>).</summary>
    public int Ragdolls { get; private set; }

    /// <summary>Ragdolls started so far (probe <c>fx.ragdolls_started</c>).</summary>
    public int RagdollsStarted { get; private set; }

    /// <summary>The last ragdoll started (probes).</summary>
    public Ragdoll? LastRagdoll { get; private set; }

    private Ragdoll? TryRagdoll(Npc npc, CreatureVisual visual)
    {
        // Physics bodies do not scale: only life-size humans ragdoll.
        if (visual.Humanoid == null || Ragdolls >= RagdollBudget || Mathf.Abs(npc.Def.Scale - 1f) > 0.1f) return null;
        if (visual.Ragdoll is not { } ragdoll) return null;
        visual.Humanoid.Animating = false;
        var v = npc.Velocity;
        var push = new Vector3(v.X, Mathf.Max(v.Y, 0f), v.Z);
        if (push.LengthSquared() > 64f) push = push.Normalized() * 8f;
        // A still body (slain without knockback) folds backwards, away from where it faced.
        var topple = new Vector3(-Mathf.Sin(npc.Yaw), 0f, -Mathf.Cos(npc.Yaw)) * 1.5f;
        ragdoll.Start(push, topple);
        LastRagdoll = ragdoll;
        Ragdolls++;
        RagdollsStarted++;
        return ragdoll;
    }

    private void EndRagdoll(Corpse c)
    {
        if (c.Ragdoll == null) return;
        c.Ragdoll.Stop();
        if (c.Visual.Humanoid != null) c.Visual.Humanoid.Animating = true;
        Ragdolls--;
    }
}
