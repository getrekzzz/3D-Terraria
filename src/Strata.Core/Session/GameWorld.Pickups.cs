using System.Numerics;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Session;

/// <summary>
/// Collecting dropped items, and pickups (P3-29): items with <see cref="Content.Defs.ItemDef.Pickup"/> (Heart, Star)
/// heal on touch and are never stored, so they are pulled in even when the inventory is full.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Terraria's Heart (+20 life) and Star (+100 mana) item ids. "star" is the mana star, not fallen_star.</summary>
    public const string HeartItem = "heart", ManaStarItem = "star";

    /// <summary>A player touched a pickup: the stack consumed and the life and mana it actually restored.</summary>
    public event Action<PlayerEntity, ItemStack, int, int>? PickupApplied;

    /// <summary>Whether the magnet pulls a stack to the player: a pickup always, anything else only if one more fits.</summary>
    public static bool CanCollect(PlayerEntity p, ItemStack stack) =>
        stack.Item?.Pickup != null || p.State.Inventory.CanTake(stack.WithCount(1));

    /// <summary>Takes a touched stack: applies a pickup, else stores it; returns what is left on the ground.</summary>
    public ItemStack Collect(PlayerEntity p, ItemStack stack)
    {
        if (stack.Item?.Pickup != null)
        {
            ApplyPickup(p, stack);
            return ItemStack.Empty;
        }
        var rest = p.State.Inventory.Pickup(stack);
        if (rest.Count < stack.Count) ItemPickedUp?.Invoke(p, stack.WithCount(stack.Count - rest.Count));
        return rest;
    }

    /// <summary>Applies a pickup stack's effect once per item (heal numbers float through <see cref="ReportHeal"/>).</summary>
    public void ApplyPickup(PlayerEntity p, ItemStack stack)
    {
        if (stack.Item?.Pickup is not { } fx) return;
        var v = p.State.Vitals;
        int life = fx.HealLife > 0 ? v.Heal(fx.HealLife * stack.Count) : 0;
        int mana = fx.HealMana > 0 ? v.RestoreMana(fx.HealMana * stack.Count) : 0;
        ReportHeal(p, life, mana: false);
        ReportHeal(p, mana, mana: true);
        PickupApplied?.Invoke(p, stack, life, mana);
    }

    /// <summary>
    /// Drops a pickup (or any item) by id with a small upward toss: the reusable path for Hearts and mana Stars from
    /// kills and other sources. Null when the item is not in the content.
    /// </summary>
    public DroppedItem? SpawnPickup(string itemId, Vector3 at, Vector3? velocity = null, int count = 1, int pickupDelay = 20)
    {
        if (!Session.Content.Items.TryGet(itemId, out var item)) return null;
        return Drops.Spawn(new ItemStack(item, count), at, velocity ?? new Vector3(0, 3f, 0), pickupDelay);
    }

    /// <summary>Drops a mana Star (+100 mana on touch, no slot needed).</summary>
    public DroppedItem? SpawnManaStar(Vector3 at, Vector3? velocity = null) => SpawnPickup(ManaStarItem, at, velocity);

    /// <summary>Drops a Heart (+20 life on touch, no slot needed).</summary>
    public DroppedItem? SpawnHeart(Vector3 at, Vector3? velocity = null) => SpawnPickup(HeartItem, at, velocity);
}
