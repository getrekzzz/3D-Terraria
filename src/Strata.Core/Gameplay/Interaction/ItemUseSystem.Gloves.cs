using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// Equipment that throws with every weapon attack (the Bone Glove's crossbones): after a swing or shot, each
    /// <c>attackProjectile</c> effect launches its projectile from the hand along the aim, then waits out its cooldown
    /// (<see cref="PlayerEntity.GloveCooldown"/>).
    /// </summary>
    private void LaunchAttackProjectiles(PlayerEntity p, ItemUseInfo weapon)
    {
        var throws = p.State.Effects.AttackProjectiles;
        if (throws.Count == 0 || p.GloveCooldown > 0 || weapon.Item.Damage <= 0) return;
        var projectiles = _world.Session.Content.Projectiles;
        int cooldown = 0;
        foreach (var t in throws)
        {
            if (!projectiles.TryGet(t.Projectile, out var def)) continue;
            var dir = p.Aim.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(p.Aim) : p.Facing;
            _world.Projectiles.Spawn(def, p.Hand, dir * t.Speed, p, t.Damage, t.Knockback, DamageClass.Generic, 4);
            cooldown = Math.Max(cooldown, t.Cooldown);
        }
        p.GloveCooldown = cooldown;
    }
}
