using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Town;
using Strata.Game.Menu;

namespace Strata.Game.UI;

/// <summary>
/// The Stylist's haircut (P5-21): a turntable preview of the character with the chosen hairstyle and colour, the
/// creator's style list and hair swatches, the price for the change, and Buy / Cancel. Opened by the 'haircut' service
/// (the W4 chat box's button; until then the console's <c>ui haircut &lt;resident&gt;</c>).
/// </summary>
public sealed partial class HaircutPanel : CanvasLayer
{
    private const string Gate = "haircut";
    private UiContext _ui = null!;
    private PanelContainer _root = null!;
    private CharacterPreview? _preview;
    private VBoxContainer _form = null!;
    private Label _price = null!;
    private Button _buy = null!;
    private TownNpc? _npc;
    private Appearance _look = new();

    public bool IsOpen => _root.Visible;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "HaircutPanel";
        Layer = 23;
        _root = new PanelContainer { Visible = false, Theme = UiTheme.Theme };
        _root.SetAnchorsPreset(Control.LayoutPreset.Center);
        _root.Position = new Vector2(-330, -230);
        AddChild(_root);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 16);
        _root.AddChild(columns);
        _form = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        _form.AddThemeConstantOverride("separation", 8);
        columns.AddChild(_form);
        // The 3D preview is built on first open: it costs a SubViewport and a character model.
        columns.AddChild(new Control { Name = "PreviewSlot", CustomMinimumSize = new Vector2(240, 420) });
    }

    /// <summary>Opens the haircut for <paramref name="npc"/> (a resident with the 'haircut' service).</summary>
    public void Open(TownNpc npc)
    {
        _npc = npc;
        _look = _ui.Entity.State.Look.Clone();
        _look.HairDye = null; // the preview shows the natural colour being bought
        if (_preview == null)
        {
            var slot = _root.GetChild(0).GetNode<Control>("PreviewSlot");
            _preview = new CharacterPreview();
            _preview.Initialize(new Vector2(240, 420));
            slot.AddChild(_preview);
        }
        BuildForm();
        Refresh();
        _root.Visible = true;
        InputGate.SetOpen(Gate, true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        _root.Visible = false;
        InputGate.SetOpen(Gate, false);
        _npc = null;
    }

    private void BuildForm()
    {
        foreach (var c in _form.GetChildren()) c.QueueFree();
        _form.AddChild(MenuKit.Text($"{_npc!.Title}: Haircut", 22, MenuKit.Gold));
        var styles = Appearance.StylesFor(_look.Body);
        int current = Math.Max(0, Array.IndexOf(styles, _look.HairStyle));
        _form.AddChild(MenuKit.Row("Hair Style", MenuKit.Choice(Array.ConvertAll(styles, Capital), current, i => { _look.HairStyle = styles[i]; Refresh(); })));
        _form.AddChild(MenuKit.Row("Hair Color", MenuKit.Swatches(Appearance.HairPalette, _look.HairColor, h => { _look.HairColor = h; Refresh(); })));
        _price = MenuKit.Text("", 18);
        _form.AddChild(_price);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        _buy = MenuKit.Small("Buy", Buy, 120);
        buttons.AddChild(_buy);
        buttons.AddChild(MenuKit.Small("Cancel", Close, 120));
        _form.AddChild(buttons);
    }

    private void Refresh()
    {
        _preview?.Show(_look);
        if (_npc == null) return;
        long cost = _ui.Game.Services.HaircutCost(_npc, _ui.Entity, _look.HairStyle, Hex(_look.HairColor));
        _price.Text = cost < 0 ? "Not available." : cost == 0 ? "No change." : $"Price: {Coins.Format(cost)}";
        _buy.Disabled = cost <= 0;
    }

    private void Buy()
    {
        if (_npc == null) return;
        var result = _ui.Game.Services.Haircut(_npc, _ui.Entity, _look.HairStyle, Hex(_look.HairColor));
        switch (result)
        {
            case TradeResult.Ok:
                _ui.Notify("Looking sharp!", MenuKit.Gold);
                Close();
                break;
            case TradeResult.TooPoor:
                _ui.Notify("You can't afford that.", new Color(1f, 0.45f, 0.4f));
                break;
            default:
                Refresh();
                break;
        }
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
        if (!IsOpen || _npc == null) return;
        var far = System.Numerics.Vector3.Distance(_npc.Center, _ui.Entity.Center) > _ui.Game.Town.Config.TalkRange + 1f;
        if (far || !_npc.Alive || !_ui.Entity.Alive) Close();
    }

    /// <summary>The colour picker reports #rrggbbaa; the look keeps #rrggbb.</summary>
    private static string Hex(string color) => color.Length == 9 ? color[..7] : color;

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');
}
