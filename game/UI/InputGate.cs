using System.Collections.Generic;
using Godot;

namespace Strata.Game.UI;

/// <summary>How much of the game an open UI layer takes over.</summary>
public enum GateKind
{
    /// <summary>The inventory, a chest, a shop, an NPC chat: the pointer is free but the player still walks and fights.</summary>
    Overlay,

    /// <summary>The pause menu and settings, the console, the world map, an auto-pausing inventory: gameplay input stops.</summary>
    Modal,
}

/// <summary>
/// Tracks open UI layers that free the mouse. A <see cref="GateKind.Modal"/> layer stops gameplay input
/// (<see cref="GameplayBlocked"/>); an <see cref="GateKind.Overlay"/> only frees the pointer, so walking, jumping, the
/// hook and the hotbar keys keep working, and the item is used only by pressing on empty screen
/// (<see cref="WorldPointer"/>, set by <see cref="WorldClickCatcher"/>), never by the clicks the panels take.
/// </summary>
public static class InputGate
{
    private static readonly HashSet<string> Modals = new();
    private static readonly HashSet<string> Overlays = new();
    private static bool _worldPointer;
    private static bool _worldPress;
    private static bool _cameraDrag;

    /// <summary>A modal layer is open (menus, the console, the map): gameplay input is ignored.</summary>
    public static bool GameplayBlocked => Modals.Count > 0;

    /// <summary>An overlay (inventory, chest, shop, NPC chat) is open: the pointer is free, gameplay goes on.</summary>
    public static bool OverlayOpen => Overlays.Count > 0;

    /// <summary>Any layer is open: the mouse is visible and its motion does not turn the camera.</summary>
    public static bool MouseFree => Modals.Count > 0 || Overlays.Count > 0;

    /// <summary>True when a text field has focus (even hotkeys must be ignored).</summary>
    public static bool TextEntry { get; set; }

    /// <summary>Walking, jumping, the hook, sprint and dashes are ignored.</summary>
    public static bool MovementBlocked => GameplayBlocked || TextEntry;

    /// <summary>The hotbar keys and wheel, the build shape keys, dropping the held item and the camera keys are ignored.</summary>
    public static bool HotkeysBlocked => GameplayBlocked || TextEntry;

    /// <summary>The held item is not used: a modal is open, or an overlay is open and the button is not held on empty screen.</summary>
    public static bool ItemUseBlocked => GameplayBlocked || TextEntry || (OverlayOpen && !_worldPointer && !_worldPress);

    /// <summary>Mouse motion does not turn the camera: the pointer is free and no right-button drag is turning it.</summary>
    public static bool LookBlocked => GameplayBlocked || (OverlayOpen && !_cameraDrag);

    /// <summary>The use button is held on empty screen while an overlay is open: the item is used toward the crosshair.</summary>
    public static bool WorldPointer
    {
        get => _worldPointer;
        set
        {
            bool held = value && OverlayOpen && !GameplayBlocked;
            if (held && !_worldPointer) _worldPress = true;
            _worldPointer = held;
        }
    }

    /// <summary>
    /// Whether the use button went down on empty screen since the last call, and clears it: a tap pressed and released
    /// within one physics tick (a quick click, or any click in a slow frame) still uses the item once.
    /// </summary>
    public static bool TakeWorldPress()
    {
        bool press = _worldPress;
        _worldPress = false;
        return press;
    }

    /// <summary>The right button drags on empty screen while an overlay is open: the camera turns.</summary>
    public static bool CameraDrag
    {
        get => _cameraDrag;
        set => _cameraDrag = value && OverlayOpen && !GameplayBlocked;
    }

    public static void SetOpen(string layer, bool open, GateKind kind = GateKind.Modal)
    {
        // A layer is one kind at a time (the inventory is a modal while it auto-pauses), and closes whatever it opened as.
        Modals.Remove(layer);
        Overlays.Remove(layer);
        if (open) (kind == GateKind.Overlay ? Overlays : Modals).Add(layer);
        // A press or drag on empty screen ends with the last overlay, or when a modal opens over it.
        if (!OverlayOpen || GameplayBlocked) _worldPointer = _worldPress = _cameraDrag = false;
        Input.MouseMode = MouseFree ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    public static bool IsOpen(string layer) => Modals.Contains(layer) || Overlays.Contains(layer);

    /// <summary>
    /// The pointer is over a panel of an open screen (slots, a scroll list, buttons): the mouse wheel belongs to the
    /// panel, not the hotbar. Empty screen (the click catcher) and the HUD do not count.
    /// </summary>
    public static bool PointerOverPanel(Viewport viewport)
    {
        if (!MouseFree) return false;
        for (Node? n = viewport.GuiGetHoveredControl(); n is Control; n = n.GetParent())
            if (n is PanelContainer or Panel or ScrollContainer) return true;
        return false;
    }

    /// <summary>Closes every layer (leaving a world for the menu).</summary>
    public static void Reset()
    {
        Modals.Clear();
        Overlays.Clear();
        TextEntry = false;
        _worldPointer = _worldPress = _cameraDrag = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
