using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>A crate, bag or present was opened from an inventory slot (for its sound).</summary>
    public event Action<PlayerEntity, ItemDef>? Opened;

    /// <summary>Whether right-clicking the item in the inventory opens it: anything whose consume behaviour has a loot table.</summary>
    public bool CanOpen(ItemDef item) => _world.Uses[item].Consume?.Loot != null;

    /// <summary>
    /// Opens one crate, treasure bag or present in place, as the reference game's inventory right-click does: one is
    /// taken from the slot and its loot table rolls into the player's storage (coins to the coin slots; what does not
    /// fit drops at the player). Returns false when the slot holds nothing that opens or the player is dead.
    /// </summary>
    public bool OpenFromSlot(PlayerEntity p, Inventory inv, int slot)
    {
        var s = inv[slot];
        if (s.IsEmpty || !p.Alive || _world.Uses[s.Item!].Consume?.Loot is not { } table) return false;
        inv.Take(slot, 1);
        Open(p, table);
        Opened?.Invoke(p, s.Item!);
        return true;
    }
}
