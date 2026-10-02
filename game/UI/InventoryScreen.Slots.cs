using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Slot views: each is made once and reads its slot live, tinted by its area (<see cref="SlotKind"/>), with its
/// silhouette while empty and the rounds left on bows and guns; its clicks go to <see cref="Click"/>
/// (InventoryScreen.Clicks.cs).
/// </summary>
public sealed partial class InventoryScreen
{
    private static readonly string[] ArmorHints = { "slot_head", "slot_body", "slot_legs" };
    private static readonly string[] MiscHints = { "slot_pet", "slot_light_pet", "slot_minecart", "slot_mount", "slot_hook" };

    private readonly List<(Func<SlotRef> Slot, ItemSlotView View)> _slots = new();

    /// <summary>The view showing <paramref name="slot"/> now (the <c>ui hover</c> command), or null.</summary>
    public ItemSlotView? FindSlot(SlotRef slot)
    {
        if (slot.Area == SlotArea.Shop) return _shop.View(slot.Index);
        foreach (var (at, view) in _slots)
            if (at() == slot) return view;
        return null;
    }

    private ItemSlotView Slot(SlotArea area, int index, string? badge = null) => Slot(() => new SlotRef(area, index), badge, Hint(area, index));

    private ItemSlotView Slot(Func<SlotRef> slot, string? badge, Texture2D? hint)
    {
        ItemSlotView view = null!;
        view = new ItemSlotView(() => Read(slot()), UiLayout.Slot, KindOf(slot().Area), _ui.Tooltip)
        {
            Badge = badge,
            EmptyHint = hint,
            RepeatRight = true,
            Favorite = () => IsFavorite(slot()),
            AmmoCount = ammoClass => _ui.Inventory.CountAmmo(ammoClass),
        };
        view.Clicked += (button, mods) => Click(view, slot(), button == MouseButton.Right, mods);
        _slots.Add((slot, view));
        return view;
    }

    /// <summary>Terraria's slot tint per area: blue backpack, teal chest, violet equipment, darker vanity and dyes.</summary>
    private static SlotKind KindOf(SlotArea area) => area switch
    {
        SlotArea.Coins => SlotKind.Coin,
        SlotArea.Ammo => SlotKind.Ammo,
        SlotArea.Trash => SlotKind.Trash,
        SlotArea.Armor or SlotArea.Accessories => SlotKind.Equip,
        SlotArea.VanityArmor or SlotArea.VanityAccessories => SlotKind.Vanity,
        SlotArea.Dyes or SlotArea.MiscDyes => SlotKind.Dye,
        SlotArea.Misc => SlotKind.Misc,
        SlotArea.Container => SlotKind.Chest,
        SlotArea.Shop => SlotKind.Shop,
        _ => SlotKind.Inventory,
    };

    /// <summary>The silhouette an empty slot shows (generated UI sprites), or null.</summary>
    private static Texture2D? Hint(SlotArea area, int index) => area switch
    {
        SlotArea.Armor or SlotArea.VanityArmor when index < ArmorHints.Length => UiSprites.Get(ArmorHints[index]),
        SlotArea.Accessories or SlotArea.VanityAccessories => UiSprites.Get("slot_accessory"),
        SlotArea.Dyes or SlotArea.MiscDyes => UiSprites.Get("slot_dye"),
        SlotArea.Misc when index < MiscHints.Length => UiSprites.Get(MiscHints[index]),
        SlotArea.Coins => UiSprites.Get("slot_coin"),
        SlotArea.Ammo => UiSprites.Get("slot_ammo"),
        SlotArea.Trash => UiSprites.Get("trash"),
        _ => null,
    };

    private ItemStack Read(SlotRef slot)
    {
        var inv = slot.Area == SlotArea.Container ? _chest.ContainerInventory : _ui.Inventory.Resolve(slot.Area);
        return inv != null && slot.Index < inv.Size ? inv[slot.Index] : ItemStack.Empty;
    }

    private bool IsFavorite(SlotRef slot) =>
        slot.Area != SlotArea.Container && _ui.Inventory.Resolve(slot.Area) is { } inv && slot.Index < inv.Size && inv.IsFavorite(slot.Index);
}
