using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

public sealed partial class ProjectileSystem
{
    /// <summary>
    /// The wearer was hurt: each <c>hurtProjectile</c> effect (the Star Cloak) calls its projectiles down from high
    /// above, scattered around the player and falling onto the ground near them (Terraria drops three stars).
    /// </summary>
    public void CallHurtProjectiles(PlayerEntity player)
    {
        var calls = player.State.Effects.HurtProjectiles;
        if (calls.Count == 0) return;
        var projectiles = _world.Session.Content.Projectiles;
        foreach (var c in calls)
        {
            if (!projectiles.TryGet(c.Projectile, out var def)) continue;
            for (int i = 0; i < c.Count; i++)
            {
                var start = player.Center + new Vector3(_rng.NextSigned() * c.Spread, c.Height, _rng.NextSigned() * c.Spread);
                var land = player.Center + new Vector3(_rng.NextSigned() * c.Spread, 0f, _rng.NextSigned() * c.Spread);
                var dir = Vector3.Normalize(land - start);
                Spawn(def, start, dir * c.Speed, player, c.Damage, c.Knockback, DamageClass.Generic, 4);
            }
        }
    }
}
