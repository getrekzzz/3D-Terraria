using Godot;
using Strata.Core.Presentation;
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
    private const int MinRenameFont = 9;

    /// <summary>How far the name field may rise above the panel: the gap between the backpack grid and the panel.</summary>
    private const float RenameHeadroom = 2 * UiLayout.Gap - 1;

    private LineEdit RenameField()
    {
        var field = new LineEdit { MaxLength = TileObjectInstance.MaxNameLength, Visible = false };
        field.AddThemeFontSizeOverride("font_size", UiFonts.Small);
        // Without the theme's 4 px padding above and below the text, so the field is as tall as the font's line and fits
        // the caption row (FitRenameField) instead of covering the first row of chest slots while a name is typed.
        foreach (string box in new[] { "normal", "focus", "read_only" })
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
        if (Container == null || Container.Def.SharedStorage != null) return; // a Piggy Bank or Safe keeps its name
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

    /// <summary>
    /// Puts the name field on the caption row, <paramref name="width"/> wide, its bottom on the row's bottom so a field
    /// taller than the row grows up into the gap above the panel rather than down over the first row of slots; in the
    /// largest font up to the small UI size whose line fits the row and that gap.
    /// </summary>
    private void FitRenameField(float width, float height)
    {
        var font = _renameField.GetThemeFont("font");
        float pad = _renameField.GetThemeStylebox("normal").GetMinimumSize().Y, room = height + RenameHeadroom;
        int size = UiFonts.Small;
        while (size > MinRenameFont && font.GetHeight(size) + pad > room) size--;
        _renameField.AddThemeFontSizeOverride("font_size", size);
        float h = Mathf.Max(height, font.GetHeight(size) + pad);
        _renameField.CustomMinimumSize = Vector2.Zero;
        _renameField.Position = new Vector2(0, height - h);
        _renameField.Size = new Vector2(width, h);
    }

    /// <summary>
    /// The name field stays on the caption row and the gap above the panel, off the first row of slots (the
    /// <c>ui.rename_fits</c> probe).
    /// </summary>
    public bool RenameFits => _renameField.Position.Y + _renameField.Size.Y <= UiLayout.Caption + 0.01f
        && _renameField.Position.Y >= -RenameHeadroom - 0.01f;
}
