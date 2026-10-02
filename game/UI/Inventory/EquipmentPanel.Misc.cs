using Godot;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// The Misc page: Terraria's pet, light pet, minecart, mount and hook slots (<see cref="MiscSlot"/> order), each with
/// its dye to the left, in the Vanity and Equip columns. The slot filters (<see cref="SlotFilters"/>) take only the
/// matching kind. The worn hook is the one the grapple key throws (<c>GrappleHook.Best</c>); the worn mount is
/// <see cref="PlayerInventory.EquippedMount"/>, which mounts read.
/// </summary>
public sealed partial class EquipmentPanel
{
    private static readonly string[] MiscNames = { "Pet", "Light Pet", "Minecart", "Mount", "Grappling Hook" };

    private readonly Control _miscPage = new() { Name = "MiscPage", MouseFilter = MouseFilterEnum.Ignore };
    private readonly ItemSlotView[] _miscDyes = new ItemSlotView[MiscSlots.Count];
    private readonly ItemSlotView[] _misc = new ItemSlotView[MiscSlots.Count];

    private void BuildMisc()
    {
        AddChild(_miscPage);
        for (int i = 0; i < MiscSlots.Count; i++)
        {
            _miscPage.AddChild(_miscDyes[i] = Named(SlotArea.MiscDyes, i, "Dye"));
            _miscPage.AddChild(_misc[i] = Named(SlotArea.Misc, i, MiscNames[i]));
        }
    }

    private void ArrangeMisc(float dyeX, float itemX, float top, float side, float pitch)
    {
        for (int i = 0; i < MiscSlots.Count; i++)
        {
            Put(_miscDyes[i], dyeX, top + i * pitch, side);
            Put(_misc[i], itemX, top + i * pitch, side);
        }
    }
}
