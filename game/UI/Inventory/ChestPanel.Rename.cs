using Godot;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// Renaming the open chest: the title row turns into a themed field holding the chest's own name (its type's name as
/// the placeholder). While it has the keyboard, <see cref="InputGate.TextEntry"/> is set and a modal 'rename' gate is
/// open, so no key moves the player or reaches a hotkey. Enter (or a click elsewhere) keeps the name through
/// <c>GameWorld.RenameContainer</c>, which saves it with the chunk; Esc cancels. An empty name restores the default.
/// </summary>
public sealed partial class ChestPanel
{
    private LineEdit RenameField()
    {
        var field = new LineEdit { MaxLength = TileObjectInstance.MaxNameLength, Visible = false };
        field.AddThemeFontSizeOverride("font_size", UiFonts.Small);
        // Without the theme's 4 px padding above and below the text, so the field (as tall as the font's line) grows
        // less far past the 18 px caption row over the first row of chest slots while a name is typed.
        foreach (string box in new[] { "normal", "focus" })
        {
            var style = (StyleBox)UiTheme.Theme.GetStylebox(box, "LineEdit").Duplicate();
            style.ContentMarginTop = style.ContentMarginBottom = 0;
            field.AddThemeStyleboxOverride(box, style);
        }
        field.FocusEntered += () =>
        {
            InputGate.TextEntry = true;
            InputGate.SetOpen(RenameGate, true, GateKind.Modal);
        };
        field.FocusExited += () =>
        {
            InputGate.TextEntry = false;
            InputGate.SetOpen(RenameGate, false, GateKind.Modal);
            FinishRename();
        };
        field.TextSubmitted += _ => field.ReleaseFocus();
        field.GuiInput += e =>
        {
            if (!e.IsActionPressed("ui_cancel")) return;
            _renameCancelled = true;
            field.AcceptEvent(); // Esc ends the typing only: it does not close the inventory or open the pause menu
            field.ReleaseFocus();
        };
        return field;
    }

    /// <summary>Shows the name field over the title with the chest's own name selected (the Rename link).</summary>
    public void StartRename()
    {
        if (Container == null) return;
        _renameCancelled = false;
        _renameField.Text = Container.CustomName ?? "";
        _renameField.PlaceholderText = Container.Def.DisplayName;
        _title.Visible = false;
        _renameField.Visible = true;
        _renameField.GrabFocus();
        _renameField.SelectAll();
    }

    /// <summary>Types <paramref name="name"/> into the open field and presses Enter (the <c>ui chest rename</c> command).</summary>
    public bool SubmitRename(string name)
    {
        if (!Renaming) return false;
        _renameField.Text = name;
        EndRename();
        return true;
    }

    /// <summary>Presses Esc in the open field (the <c>ui chest cancel</c> command): the old name stays.</summary>
    public bool CancelRename()
    {
        if (!Renaming) return false;
        _renameCancelled = true;
        EndRename();
        return true;
    }

    /// <summary>Ends typing and keeps the name: losing the keyboard commits it (and closes the gate).</summary>
    private void EndRename()
    {
        if (_renameField.HasFocus()) _renameField.ReleaseFocus();
        else FinishRename();
    }

    private void FinishRename()
    {
        if (!_renameField.Visible) return;
        _renameField.Visible = false;
        _title.Visible = true;
        if (Container == null || _renameCancelled) return;
        _ui.Game.RenameContainer(Container, _renameField.Text);
        _title.Text = Container.DisplayName;
    }
}
