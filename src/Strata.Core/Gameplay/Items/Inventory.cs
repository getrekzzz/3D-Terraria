using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// A fixed-size container of item stacks (player backpack, chests). Slot 0–9 of the player inventory form
/// the hotbar. All operations are exact: they return what could not be stored instead of dropping it.
/// </summary>
/// <remarks>
/// Favourites belong to the slot, not to <see cref="ItemStack"/> (whose equality and stacking stay untouched): only the
/// player's own storages keep them. A slot loses its mark when it empties or another item replaces its stack, and
/// <see cref="Transfer"/> carries the mark along; <see cref="Add"/> never sets one, so items stored in a chest drop it.
/// </remarks>
public sealed class Inventory
{
    private readonly ItemStack[] _slots;
    private readonly bool[]? _favorite;

    public Inventory(int size, bool keepsFavorites = false)
    {
        _slots = new ItemStack[size];
        if (keepsFavorites) _favorite = new bool[size];
    }

    public Inventory(ItemStack[] backing) => _slots = backing;

    public int Size => _slots.Length;

    public ItemStack this[int slot]
    {
        get => _slots[slot];
        set
        {
            if (_favorite != null && (value.IsEmpty || value.Item != _slots[slot].Item)) _favorite[slot] = false;
            _slots[slot] = value.IsEmpty ? ItemStack.Empty : value;
            Changed?.Invoke(slot);
        }
    }

    /// <summary>Whether slots here can be favourited (the backpack, coin and ammo slots).</summary>
    public bool KeepsFavorites => _favorite != null;

    public bool IsFavorite(int slot) => _favorite != null && _favorite[slot];

    /// <summary>Marks or unmarks a slot's stack as a favourite; ignored for empty slots and storages without favourites.</summary>
    public void SetFavorite(int slot, bool favorite)
    {
        if (_favorite != null) _favorite[slot] = favorite && !_slots[slot].IsEmpty;
    }

    public event Action<int>? Changed;

    public IEnumerable<ItemStack> Stacks => _slots.Where(s => !s.IsEmpty);

    /// <summary>Adds a stack, merging into existing stacks first, then empty slots. Returns the remainder.</summary>
    public ItemStack Add(ItemStack stack, int firstSlot = 0)
    {
        if (stack.IsEmpty) return ItemStack.Empty;
        int remaining = stack.Count;
        if (stack.Item!.MaxStack > 1)
        {
            for (int i = firstSlot; i < _slots.Length && remaining > 0; i++)
            {
                var s = _slots[i];
                if (!s.CanStackWith(stack)) continue;
                int moved = Math.Min(remaining, s.SpaceLeft);
                if (moved <= 0) continue;
                this[i] = s.WithCount(s.Count + moved);
                remaining -= moved;
            }
        }
        for (int i = firstSlot; i < _slots.Length && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            int moved = Math.Min(remaining, stack.Item.MaxStack);
            this[i] = new ItemStack(stack.Item, moved, stack.Prefix);
            remaining -= moved;
        }
        return stack.WithCount(remaining);
    }

    /// <summary>Would <see cref="Add"/> store the whole stack?</summary>
    public bool CanFit(ItemStack stack)
    {
        if (stack.IsEmpty) return true;
        int space = 0;
        foreach (var s in _slots)
        {
            if (s.IsEmpty) space += stack.Item!.MaxStack;
            else if (s.CanStackWith(stack)) space += s.SpaceLeft;
            if (space >= stack.Count) return true;
        }
        return false;
    }

    public int Count(ItemDef item)
    {
        int n = 0;
        foreach (var s in _slots)
            if (s.Item == item) n += s.Count;
        return n;
    }

    public int Count(Func<ItemDef, bool> match)
    {
        int n = 0;
        foreach (var s in _slots)
            if (!s.IsEmpty && match(s.Item!)) n += s.Count;
        return n;
    }

    /// <summary>Removes up to <paramref name="count"/> items matching; returns how many were removed.</summary>
    public int Remove(Func<ItemDef, bool> match, int count)
    {
        int removed = 0;
        for (int i = _slots.Length - 1; i >= 0 && removed < count; i--)
        {
            var s = _slots[i];
            if (s.IsEmpty || !match(s.Item!)) continue;
            int take = Math.Min(s.Count, count - removed);
            this[i] = s.WithCount(s.Count - take);
            removed += take;
        }
        return removed;
    }

    public int Remove(ItemDef item, int count) => Remove(d => d == item, count);

    /// <summary>Removes <paramref name="count"/> from a specific slot; returns the removed stack.</summary>
    public ItemStack Take(int slot, int count)
    {
        var s = _slots[slot];
        if (s.IsEmpty) return ItemStack.Empty;
        int take = Math.Min(count, s.Count);
        this[slot] = s.WithCount(s.Count - take);
        return s.WithCount(take);
    }

    /// <summary>Swaps or merges the contents of two slots (possibly in different inventories); favourite marks move with the stacks.</summary>
    public static void Transfer(Inventory from, int fromSlot, Inventory to, int toSlot)
    {
        var a = from[fromSlot];
        var b = to[toSlot];
        if (a.CanStackWith(b))
        {
            // Both slots keep their own marks: the stacks only change size.
            int moved = Math.Min(a.Count, b.SpaceLeft);
            to[toSlot] = b.WithCount(b.Count + moved);
            from[fromSlot] = a.WithCount(a.Count - moved);
            return;
        }
        bool favA = from.IsFavorite(fromSlot), favB = to.IsFavorite(toSlot);
        to[toSlot] = a;
        from[fromSlot] = b;
        to.SetFavorite(toSlot, favA);
        from.SetFavorite(fromSlot, favB);
    }

    public ItemStack[] ToArray() => (ItemStack[])_slots.Clone();

    public void Clear()
    {
        for (int i = 0; i < _slots.Length; i++) this[i] = ItemStack.Empty;
    }
}
