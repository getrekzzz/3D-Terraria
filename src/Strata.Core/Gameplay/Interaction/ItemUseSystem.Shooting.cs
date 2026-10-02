using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// Fires the next shot of a burst without input (Terraria keeps shooting while the animation runs and the use time
    /// is up): a gun whose use time is shorter than its animation, such as the Clockwork Assault Rifle, always
    /// finishes a burst it started. Called every tick after the use timers count down.
    /// </summary>
    public void Continue(PlayerEntity p)
    {
        if (p.UseAnimation <= 0 || p.UseCooldown > 0 || !p.Alive || p.State.Effects.NoItemUse) return;
        var held = p.State.Inventory.Held;
        if (held.IsEmpty || held.Item != p.UseItem) return;
        var info = _world.Uses[held.Item!];
        if (!info.Has(ItemAction.Shoot) || info.IsTool || info.Has(ItemAction.Melee | ItemAction.Thrust)) return;
        float speed = UseSpeed(p, held, info);
        p.UseCooldown = UseTicks(info.Item.UseTime / speed); // a shot that cannot fire still waits its turn
        var outcome = Shoot(p, info, followUp: true);
        if (outcome == UseOutcome.Shot) Used?.Invoke(p, info, outcome);
    }

    /// <param name="followUp">A later shot of the same animation (burst): <see cref="ItemDef.AmmoPerBurst"/> guns fire it without using ammo.</param>
    private UseOutcome Shoot(PlayerEntity p, ItemUseInfo info, bool followUp = false)
    {
        var item = info.Item;
        var state = p.State;
        var projectile = info.Projectile;
        var shot = info.Shoot!;
        ItemDef? ammo = null;
        (Inventory Inv, int Slot)? ammoSlot = null;
        if (item.UseAmmo != null)
        {
            ammoSlot = state.Inventory.FindAmmo(item.UseAmmo);
            if (ammoSlot is not { } found) return UseOutcome.NoAmmo;
            ammo = found.Inv[found.Slot].Item!;
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
        // Ammo is only used up by a shot that actually fires (and, for a per-burst gun, only by the burst's first).
        if (ammo is { Consumable: true } && !(followUp && item.AmmoPerBurst) && ammoSlot is { } from && !_ammoRng.Chance(state.Effects.Stats.Bonus(StatIds.AmmoSave) + item.AmmoSaveChance)) from.Inv.Take(from.Slot, 1);
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

    /// <summary>Slot of the first stack of the given ammo class in one storage, or −1 (a player's shots use <see cref="PlayerInventory.FindAmmo"/>).</summary>
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
