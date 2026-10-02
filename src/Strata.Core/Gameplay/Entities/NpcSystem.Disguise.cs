using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Physics;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Disguised creatures (<see cref="Content.Defs.DisguiseInfo"/>): a Mimic sits as a chest and the Nymph stands as a
/// Lost Girl until a player hits it, tries to open it, or (with a wake range) comes too close; then it is itself for good.
/// </summary>
public sealed partial class NpcSystem
{
    /// <summary>A disguised creature woke (the renderer drops the disguise; the HUD may name it).</summary>
    public event Action<Npc>? Unmasked;

    /// <summary>
    /// The disguised creature's tick: it wakes if the nearest player came within its wake range, otherwise it only
    /// settles under gravity — no thinking, contact damage or volleys. Returns true while it stays disguised.
    /// </summary>
    private bool SitDisguised(Npc npc, PlayerEntity? target, float distance, float dt)
    {
        var disguise = npc.Def.Disguise!;
        if (target != null && disguise.WakeRange > 0 && distance <= disguise.WakeRange)
        {
            Wake(npc, target);
            return false;
        }
        npc.Velocity.X = npc.Velocity.Z = 0f;
        npc.Pose = "idle";
        if (npc.Def.Gravity) npc.Velocity.Y = MathF.Max(npc.Velocity.Y - BrainContext.Gravity * dt, -30f);
        var center = npc.Center;
        if (npc.Def.CollideTerrain) npc.OnGround = VoxelMotion.Move(_world.Session.World, ref center, ref npc.Velocity, npc.HalfExtents, dt);
        else center += npc.Velocity * dt;
        npc.Position = center - new Vector3(0, npc.HalfExtents.Y, 0);
        return true;
    }

    /// <summary>
    /// A player tried to open or talk to a disguised creature: it wakes when its disguise reacts to interaction.
    /// Returns false for anything not (or no longer) disguised.
    /// </summary>
    public bool Provoke(Npc npc, PlayerEntity player)
    {
        if (!npc.Alive || !npc.Disguised || !npc.Def.Disguise!.WakeOnInteract) return false;
        Wake(npc, player);
        return true;
    }

    /// <summary>Drops the disguise for good: cues its sound and springs at the waker when it lunges.</summary>
    private void Wake(Npc npc, PlayerEntity? waker)
    {
        if (!npc.Disguised) return;
        var disguise = npc.Def.Disguise!;
        npc.Disguised = false;
        npc.Pose = "attack";
        if (waker != null)
        {
            npc.TargetPlayer = 0;
            var toward = BrainFactory.Flat(waker.Center - npc.Center);
            if (toward != Vector3.Zero) npc.Yaw = BrainFactory.YawOf(toward);
            if (disguise.Lunge > 0) npc.Velocity = toward * disguise.Lunge + new Vector3(0, disguise.Lunge * 0.8f, 0);
        }
        if (disguise.WakeSound != null) Cued?.Invoke(npc, disguise.WakeSound);
        Unmasked?.Invoke(npc);
    }
}
