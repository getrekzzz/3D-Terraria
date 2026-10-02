using Godot;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// Reading a sign (X-03): its text in a small editable box (up to six lines), Save writes it into the sign (saved with
/// the chunk), Close or Esc leaves it as it was. Closes when the player walks away or the sign is gone.
/// </summary>
public sealed partial class SignDialog : CanvasLayer
{
    private const string Gate = "sign";
    private const float CloseDistance = 6f;

    private UiContext _ui = null!;
    private PanelContainer _root = null!;
    private TextEdit _text = null!;
    private TileObjectInstance? _sign;

    public bool IsOpen => _root.Visible;

    /// <summary>Signs edited so far (probe <c>signs.saved</c>).</summary>
    public int Saved { get; private set; }

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "SignDialog";
        Layer = 22;
        _root = new PanelContainer { Visible = false, Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(460, 0) };
        _root.SetAnchorsPreset(Control.LayoutPreset.Center);
        _root.Position = new Vector2(-230, -120);
        AddChild(_root);
        var box = new VBoxContainer();
        var title = new Label { Text = "Sign" };
        title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(title);
        _text = new TextEdit
        {
            CustomMinimumSize = new Vector2(440, 150),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "Write something…",
        };
        box.AddChild(_text);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var save = new Button { Text = "Save" };
        save.Pressed += Save;
        var close = new Button { Text = "Close" };
        close.Pressed += Close;
        buttons.AddChild(save);
        buttons.AddChild(close);
        box.AddChild(buttons);
        _root.AddChild(box);
        ui.Game.Interaction.SignOpened += (p, sign) =>
        {
            if (p == ui.Entity) Open(sign);
        };
    }

    public void Open(TileObjectInstance sign)
    {
        _sign = sign;
        _text.Text = sign.SignText ?? "";
        _root.Visible = true;
        InputGate.SetOpen(Gate, true);
        _text.GrabFocus();
    }

    private void Save()
    {
        if (_sign != null && _ui.Game.EditSign(_sign, _text.Text)) Saved++;
        Close();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _root.Visible = false;
        _sign = null;
        InputGate.SetOpen(Gate, false);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (IsOpen && e.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _sign == null) return;
        var c = _sign.Anchor;
        var at = new System.Numerics.Vector3(c.X, c.Y, c.Z) * Strata.Core.Common.Units.CellSize;
        bool gone = _ui.Game.Session.World.TileObjects.Get(_sign.Id) != _sign;
        if (gone || System.Numerics.Vector3.Distance(at, _ui.Entity.Center) > CloseDistance) Close();
    }
}
