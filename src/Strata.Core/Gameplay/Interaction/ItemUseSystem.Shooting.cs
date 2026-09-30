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
        ItemDef? ammo = null;
        if (item.UseAmmo != null)
        {
            int slot = FindAmmo(state.Inventory.Backpack, item.UseAmmo);
            if (slot < 0) return UseOutcome.NoAmmo;
            ammo = state.Inventory.Backpack[slot].Item!;
            projectile ??= _world.Uses[ammo].Projectile;
            if (projectile == null) return UseOutcome.NoAmmo;
            if (ammo.Consumable && !_ammoRng.Chance(state.Effects.Stats.Bonus(StatIds.AmmoSave))) state.Inventory.Backpack.Take(slot, 1);
        }
        if (projectile == null) return UseOutcome.None;
        // Boomerangs and flails: one out at a time.
        if (projectile.Returns > 0 && _world.Projectiles.All.Any(x => x.Owner == p && x.Def == projectile)) return UseOutcome.Blocked;
        var held = state.Inventory.Held.Item == item ? state.Inventory.Held : new ItemStack(item, 1);
        if (item.ManaCost > 0)
        {
            int cost = (int)MathF.Round(Prefixes.ManaCost(held) * state.Effects.Stats.Get(StatIds.ManaCost, 1f));
            if (!state.Vitals.TrySpendMana(cost)) return UseOutcome.NoMana;
        }
        int damage = Prefixes.Damage(held) + (ammo?.Damage ?? 0);
        float knockback = Prefixes.Knockback(held) + (ammo?.Knockback ?? 0f);
        var shot = info.Shoot!;
        for (int i = 0; i < shot.Count; i++)
            Shot?.Invoke(new ShotRequest(p, info, projectile, ammo, Prefixes.ShootSpeed(held), damage, knockback, Prefixes.Crit(held)));
        if (item.Consumable && item.UseAmmo == null) ConsumeHeld(p); // thrown: bombs, shuriken, knives
        return UseOutcome.Shot;
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
