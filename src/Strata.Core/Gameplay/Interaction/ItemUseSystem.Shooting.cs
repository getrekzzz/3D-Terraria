using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    private UseOutcome Shoot(PlayerEntity p, ItemUseInfo info)
    {
        var item = info.Item;
        var state = p.State;
        var projectile = info.Projectile;
        var shot = info.Shoot!;
        ItemDef? ammo = null;
        int ammoSlot = -1;
        if (item.UseAmmo != null)
        {
            ammoSlot = FindAmmo(state.Inventory.Backpack, item.UseAmmo);
            if (ammoSlot < 0) return UseOutcome.NoAmmo;
            ammo = state.Inventory.Backpack[ammoSlot].Item!;
            projectile ??= _world.Uses[ammo].Projectile;
            if (projectile == null) return UseOutcome.NoAmmo;
        }
        if (projectile == null) return UseOutcome.None;
        // Boomerangs and flails: one out at a time unless the weapon says otherwise (Zenith: no limit).
        int limit = shot.Limit(projectile.Returns);
        if (limit > 0 && LiveShots(p, projectile) + shot.Count > limit) return UseOutcome.Blocked;
        var held = state.Inventory.Held.Item == item ? state.Inventory.Held : new ItemStack(item, 1);
        if (item.ManaCost > 0)
        {
            int cost = (int)MathF.Round(Prefixes.ManaCost(held) * state.Effects.Stats.Get(StatIds.ManaCost, 1f));
            if (!state.Vitals.TrySpendMana(cost)) return UseOutcome.NoMana;
        }
        // Ammo is only used up by a shot that actually fires.
        if (ammo is { Consumable: true } && !_ammoRng.Chance(state.Effects.Stats.Bonus(StatIds.AmmoSave))) state.Inventory.Backpack.Take(ammoSlot, 1);
        int damage = Prefixes.Damage(held) + (ammo?.Damage ?? 0);
        float knockback = Prefixes.Knockback(held) + (ammo?.Knockback ?? 0f);
        for (int i = 0; i < shot.Count; i++)
            Shot?.Invoke(new ShotRequest(p, info, projectile, ammo, Prefixes.ShootSpeed(held), damage, knockback, Prefixes.Crit(held)));
        if (item.Consumable && item.UseAmmo == null) ConsumeHeld(p); // thrown: bombs, shuriken, knives
        return UseOutcome.Shot;
    }

    /// <summary>How many projectiles of <paramref name="def"/> the player has out (flying or on their way back).</summary>
    private int LiveShots(PlayerEntity p, ProjectileDef def)
    {
        int n = 0;
        var all = _world.Projectiles.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Owner == p && all[i].Def == def) n++;
        return n;
    }

    /// <summary>Backpack slot of the first stack of the given ammo class, or −1.</summary>
    public static int FindAmmo(Inventory backpack, string ammoClass)
    {
        for (int i = 0; i < backpack.Size; i++)
        {
            var s = backpack[i];
            if (!s.IsEmpty && s.Item!.AmmoClass == ammoClass) return i;
        }
        return -1;
    }
}
