using Godot;

namespace Strata.Game.UI;

/// <summary>
/// The empty screen around the inventory, a chest, a shop or an NPC chat: a full-screen control on its own layer below
/// the HUD and every overlay (<see cref="LayerIndex"/>), shown while one is open, so it takes exactly the clicks no
/// panel or slot took. With a stack on the cursor a left click throws it where the player looks (right click: one).
/// With an empty cursor a held left button uses the item toward the crosshair (<see cref="InputGate.WorldPointer"/>)
/// and a right drag turns the camera (<see cref="InputGate.CameraDrag"/>). The wheel steps the hotbar.
/// </summary>
public sealed partial class WorldClickCatcher : Control
{
    /// <summary>Under the HUD (10), whose hotbar slots keep their clicks and tooltips, and so under every overlay.</summary>
    public const int LayerIndex = 9;

    /// <summary>The catcher's node path under the world runtime (the <c>ui click</c> command and probes find it there).</summary>
    public const string NodePath = "WorldClicks/WorldClickCatcher";

    private UiContext _ui = null!;
    private bool _shown;

    /// <summary>Mouse presses that landed on empty screen, any button (the <c>ui.world_clicks</c> probe).</summary>
    public int Presses { get; private set; }

    /// <summary>Builds the catcher on its own canvas layer under <paramref name="parent"/>.</summary>
    public static WorldClickCatcher Create(UiContext ui, Node parent)
    {
        var layer = new CanvasLayer { Name = "WorldClicks", Layer = LayerIndex };
        parent.AddChild(layer);
        var catcher = new WorldClickCatcher { _ui = ui, Name = "WorldClickCatcher", MouseFilter = MouseFilterEnum.Stop, Visible = false };
        catcher.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(catcher);
        return catcher;
    }

    public override void _Process(double delta)
    {
        bool show = InputGate.OverlayOpen && !InputGate.GameplayBlocked;
        if (show == _shown) return;
        _shown = show;
        Visible = show;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true }) Presses++;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mb:
                AcceptEvent();
                // A click on the world ends typing in a panel (the chest name), which would keep the player still.
                if (mb.Pressed) GetViewport().GuiReleaseFocus();
                Press(mb.ButtonIndex == MouseButton.Right, mb.Pressed);
                break;
            case InputEventMouseMotion motion when InputGate.CameraDrag:
                AcceptEvent();
                _ui.Player.Rig.AddLook(motion.Relative);
                break;
            case InputEventMouseButton wheel when _ui.Interaction.ScrollHotbar(wheel):
                AcceptEvent();
                break;
        }
    }

    private void Press(bool right, bool pressed)
    {
        if (pressed && !_ui.Inventory.Cursor.IsEmpty)
        {
            if (_ui.Game.ThrowCursor(_ui.Entity, one: right) != Strata.Core.Gameplay.Items.ClickOutcome.None) Audio.Sfx.Play("grab");
            return;
        }
        if (right) InputGate.CameraDrag = pressed;
        else InputGate.WorldPointer = pressed;
    }
}
