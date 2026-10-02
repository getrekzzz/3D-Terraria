using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Talking to a resident, as Terraria's chat box: a dark-blue box at the top centre (the layout's
/// <see cref="UiRegion.NpcChat"/> rectangle, clear of the hotbar, buffs and hearts) with the resident's name, their
/// line wrapped to the box, and under it plain text buttons that turn gold and grow under the mouse: Shop, their
/// service (TownDialog.Services.cs: Heal with its price, Help, Reforge, Taxes, Quest, a summon) and Close. The box
/// grows down from the top to fit its text and never spills off its region. It is an overlay: the player keeps moving,
/// the resident waits facing them while it is open, and walking away (or Esc) closes it. Shop hides the box and asks
/// for the shop in the inventory (<see cref="ShopRequested"/>); the conversation goes on there.
/// </summary>
public sealed partial class TownDialog : CanvasLayer
{
    private const string Gate = "town";
    private const float HoverScale = 1.1f;
    private const double HoverSeconds = 0.08;
    private const double LabelRefreshSeconds = 0.25;
    private const int NameSize = 14;
    private const int Separation = 4;

    private static readonly Color Rim = Color.Color8(120, 140, 220);

    private readonly List<Button> _buttonList = new();
    private UiContext _ui = null!;
    private VBoxContainer _host = null!;
    private PanelContainer _root = null!;
    private Label _title = null!;
    private Label _text = null!;
    private HFlowContainer _buttons = null!;
    private Button? _serviceButton;
    private TownNpc? _npc;
    private UiRect? _region;
    private double _labelWait;

    public bool IsOpen => _root.Visible;
    public TownNpc? Resident => _npc;

    /// <summary>The line shown in the box (services answer in it).</summary>
    public string Text => _text.Text;

    /// <summary>The box opened or closed: the HUD lays the screen out at once, so no frame shows it unplaced.</summary>
    public event Action? LayoutChanged;

    /// <summary>Shop was pressed: the inventory opens with this resident's shop in its panel host.</summary>
    public event Action<TownNpc>? ShopRequested;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "TownDialog";
        Layer = 22;
        // The host spans the region and passes clicks through; the box in it takes the width and its content's height.
        _host = new VBoxContainer { Name = "NpcChatRegion", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_host);
        _root = LayoutWidgets.Register(new PanelContainer { Name = "NpcChat", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop });
        _root.AddThemeStyleboxOverride("panel", ChatStyle());
        _host.AddChild(_root);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Separation);
        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", NameSize);
        _title.AddThemeColorOverride("font_color", UiTheme.Muted);
        box.AddChild(_title);
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.AddThemeFontSizeOverride("font_size", UiFonts.Body);
        box.AddChild(_text);
        _buttons = new HFlowContainer();
        _buttons.AddThemeConstantOverride("h_separation", 22);
        box.AddChild(_buttons);
        _root.AddChild(box);
        ui.Interaction.TalkRequested += Open;
    }

    public void Open(TownNpc npc)
    {
        // The interact key works with the chat open: talking to someone else lets the first resident go.
        if (_npc != null && _npc != npc) _ui.Game.Town.EndTalk(_npc);
        if (npc.TalkingTo != _ui.Entity) _ui.Game.Town.BeginTalk(npc, _ui.Entity);
        _npc = npc;
        bool opening = !IsOpen;
        _root.Visible = true;
        InputGate.SetOpen(Gate, true, GateKind.Overlay); // the player can still walk off and fight
        _title.Text = npc.Title;
        _text.Text = _ui.Game.Commerce.Greet(npc, _ui.Entity);
        RebuildButtons();
        if (opening) LayoutChanged?.Invoke();
    }

    /// <summary>Closes the box and lets the resident go.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        var npc = _npc;
        HideBox();
        if (npc != null && npc.TalkingTo == _ui.Entity) _ui.Game.Town.EndTalk(npc);
    }

    /// <summary>Shop: the box hides and the inventory shows the resident's wares (commands and tests press it too).</summary>
    public void OpenShop()
    {
        if (_npc is not { } npc || npc.Def.Shop.Length == 0) return;
        HideBox();
        ShopRequested?.Invoke(npc);
    }

    /// <summary>Puts the box on the layout's NPC chat region: its width, growing down from the top to fit its text.</summary>
    public void Place(UiLayoutResult layout)
    {
        _region = layout.TryGet(UiRegion.NpcChat, out var r) ? r : null;
        Arrange();
    }

    /// <summary>True while the box is open in the top third of the screen, overlapping no other HUD widget.</summary>
    public bool AtTopClear()
    {
        if (!IsOpen || !_root.IsVisibleInTree()) return false;
        float height = LayoutWidgets.LogicalSize(_root.GetViewport()).Y;
        return _root.GetGlobalRect().Position.Y < height / 3f && LayoutWidgets.CountOverlapsWith(_root.GetTree(), _root) == 0;
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
        var town = _ui.Game.Town;
        var far = System.Numerics.Vector3.Distance(_npc.Center, _ui.Entity.Center) > town.Config.TalkRange + 1f;
        if (far || !_npc.Alive || !_ui.Entity.Alive || !town.Residents.Contains(_npc))
        {
            Close();
            return;
        }
        town.HoldTalk(_npc, _ui.Entity);
        // A price on the service button follows the player (the Nurse's heal shrinks as life regenerates).
        if ((_labelWait -= delta) > 0 || _serviceButton == null) return;
        _labelWait = LabelRefreshSeconds;
        if (ServiceLabel(_npc) is { } label && label != _serviceButton.Text) SetLabel(_serviceButton, label);
    }

    /// <summary>Hides the box without ending the conversation (the shop carries it on).</summary>
    private void HideBox()
    {
        _root.Visible = false;
        _npc = null;
        _serviceButton = null;
        InputGate.SetOpen(Gate, false, GateKind.Overlay);
        LayoutChanged?.Invoke();
    }

    /// <summary>Shows a line in the box, which then fits its height to the text.</summary>
    private void Say(string text)
    {
        _text.Text = text;
        Arrange();
    }

    /// <summary>The buttons from the resident's definition: Shop when they sell anything, their service, Close.</summary>
    private void RebuildButtons()
    {
        foreach (var b in _buttonList)
        {
            _buttons.RemoveChild(b);
            b.QueueFree();
        }
        _buttonList.Clear();
        _serviceButton = null;
        var npc = _npc!;
        if (npc.Def.Shop.Length > 0) AddButton("Shop", OpenShop);
        if (ServiceLabel(npc) is { } label) _serviceButton = AddButton(label, RunService);
        AddButton("Close", Close);
        _labelWait = LabelRefreshSeconds;
        Arrange();
    }

    private Button AddButton(string text, Action pressed)
    {
        // No keyboard focus: the player walks and jumps while talking, and Space (ui_accept) would press it again.
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = UiTheme.TextButton };
        b.AddThemeFontSizeOverride("font_size", UiFonts.Body);
        b.MouseEntered += () =>
        {
            Grow(b, HoverScale);
            Audio.Sfx.Play("ui_hover");
        };
        b.MouseExited += () => Grow(b, 1f);
        b.Pressed += () =>
        {
            Audio.Sfx.Play("ui_click");
            pressed();
        };
        _buttons.AddChild(b);
        _buttonList.Add(b);
        return b;
    }

    /// <summary>A new price on a button: it grows about its new centre.</summary>
    private static void SetLabel(Button b, string text)
    {
        b.Text = text;
        b.ResetSize();
        b.PivotOffset = b.GetCombinedMinimumSize() / 2f;
    }

    /// <summary>Terraria's hover: the word grows about its centre.</summary>
    private static void Grow(Button b, float scale)
    {
        b.PivotOffset = b.Size / 2f;
        b.CreateTween().TweenProperty(b, "scale", new Vector2(scale, scale), HoverSeconds);
    }

    /// <summary>
    /// The box at the top of its region, as wide as it, and as tall as its content (the host container shrinks it to
    /// fit as the text changes): the line is cut to the rows that fit under the name and over the buttons, so the box
    /// never spills past the region onto the HUD.
    /// </summary>
    private void Arrange()
    {
        if (_region is not { } r) return;
        _host.Position = new Vector2(r.X, r.Y);
        _host.Size = new Vector2(r.W, r.H);
        float chrome = _root.GetThemeStylebox("panel").GetMinimumSize().Y + 2 * Separation
            + _title.GetCombinedMinimumSize().Y + _buttons.GetCombinedMinimumSize().Y;
        float line = _text.GetLineHeight() + _text.GetThemeConstant("line_spacing");
        _text.MaxLinesVisible = Math.Max(1, (int)((r.H - chrome) / Math.Max(1f, line)));
    }

    /// <summary>Terraria's chat box: deep blue, a little see-through, with a light rim.</summary>
    private static StyleBoxFlat ChatStyle()
    {
        var style = new StyleBoxFlat { BgColor = UiTheme.ChatPanel, BorderColor = Rim, AntiAliasing = true };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        style.ContentMarginLeft = style.ContentMarginRight = 14;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        return style;
    }
}
