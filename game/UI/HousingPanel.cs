using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Town;
using Strata.Core.Presentation;
using Strata.Game.Entities;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// The housing menu (P5-29): a banner (head portrait) per resident, greyed while dead or homeless; '?' then a click on
/// a room says whether it is suitable or what it lacks; a banner then a room moves that resident in (swapping with the
/// occupant); right-clicking a banner evicts. While open, banners hang in every occupied room. Opened by the inventory
/// equipment column's Housing tab, the Housing key (unbound by default; Settings > Controls) or <c>ui housing</c>.
/// </summary>
public sealed partial class HousingPanel : CanvasLayer
{
    private const string Gate = "housing";

    /// <summary>Terraria's housing message yellow.</summary>
    public static readonly Color MessageColor = Color.Color8(255, 240, 20);

    private enum Mode { None, Query, Assign }

    private UiContext _ui = null!;
    private HousingBannerRenderer _banners = null!;
    private PanelContainer _root = null!;
    private Control _catcher = null!;
    private Control? _anchor;
    private bool _anchored;
    private HFlowContainer _grid = null!;
    private Label _hint = null!;
    private Mode _mode;
    private TownNpc? _assigning;
    private readonly List<TownNpc> _shown = new();
    private double _refresh;

    public bool IsOpen => _root.Visible;

    /// <summary>Banners in the menu (probe).</summary>
    public int BannerCount => _shown.Count;

    public void Initialize(UiContext ui, HousingBannerRenderer banners)
    {
        _ui = ui;
        _banners = banners;
        Name = "HousingPanel";
        Layer = 24; // over the inventory and the chat box: while picking a room its catcher takes the world clicks
        // While picking (query or assign) a transparent full-screen catcher takes the next click on the world.
        _catcher = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += OnCatcherInput;
        AddChild(_catcher);
        _root = new PanelContainer { Visible = false, Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(420, 0) };
        _root.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _root.Position = new Vector2(-210, 70);
        AddChild(_root);
        var box = new VBoxContainer();
        _root.AddChild(box);
        var top = new HBoxContainer();
        var title = new Label { Text = "Housing", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        top.AddChild(title);
        var query = new Button { Text = "?", TooltipText = "Housing query: click a room", CustomMinimumSize = new Vector2(36, 36) };
        query.Pressed += () => SetMode(Mode.Query, null);
        top.AddChild(query);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += Close;
        top.AddChild(close);
        box.AddChild(top);
        _grid = new HFlowContainer { CustomMinimumSize = new Vector2(400, 56) };
        box.AddChild(_grid);
        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _hint.AddThemeColorOverride("font_color", UiTheme.Muted);
        box.AddChild(_hint);
    }

    /// <summary>
    /// Shows the menu under <paramref name="anchor"/> (the inventory's equipment column, whose Housing tab opens it), or
    /// top centre without one.
    /// </summary>
    public void SetAnchor(Control? anchor) => _anchor = anchor;

    public void Open()
    {
        if (IsOpen) return;
        _anchored = _anchor is { } a0 && a0.IsVisibleInTree();
        if (_anchored && _anchor is { } a)
        {
            var r = a.GetGlobalRect();
            _root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            _root.Position = new Vector2(r.Position.X + r.Size.X - _root.CustomMinimumSize.X, r.Position.Y + UiLayout.SortButton + UiLayout.Gap);
        }
        else
        {
            _root.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            _root.Position = new Vector2(-210, 70);
        }
        PortraitIcons.Preload(Residents().Select(r => Strata.Core.Content.AssetPaths.TownNpcHead(r.Def)));
        _root.Visible = true;
        InputGate.SetOpen(Gate, true, GateKind.Overlay);
        _banners.SetShown(true);
        SetMode(Mode.None, null);
        Rebuild();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _root.Visible = false;
        InputGate.SetOpen(Gate, false, GateKind.Overlay);
        _banners.SetShown(false);
        SetMode(Mode.None, null);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>The housing query for a cell, told to the player in the housing yellow; returns the message.</summary>
    public string Query(Int3 cell)
    {
        var q = _ui.Game.Town.QueryHousing(cell);
        _ui.Notify(q.Message, MessageColor);
        return q.Message;
    }

    /// <summary>Moves <paramref name="npc"/> into the room at <paramref name="cell"/>; returns the message shown.</summary>
    public string Assign(TownNpc npc, Int3 cell)
    {
        var result = _ui.Game.Town.AssignHome(npc, cell, out var message);
        _ui.Notify(message, MessageColor);
        Rebuild();
        return $"{result}: {message}";
    }

    public void Evict(TownNpc npc)
    {
        if (_ui.Game.Town.Evict(npc)) _ui.Notify($"{npc.Title} was evicted.", MessageColor);
        Rebuild();
    }

    /// <summary>Residents who can have a house: alive or awaiting their respawn, not bound, not landmark keepers.</summary>
    private IEnumerable<TownNpc> Residents() =>
        _ui.Game.Town.Residents.Where(r => r.Def.Kind == TownNpcKind.Resident && !r.Bound);

    private int _signature;

    /// <summary>Who is listed and whether each is alive and housed (a change rebuilds the banners).</summary>
    private int Signature()
    {
        int sig = 17;
        foreach (var r in Residents()) sig = sig * 31 + r.GetHashCode() * 4 + (r.Alive ? 2 : 0) + (r.Home != null ? 1 : 0);
        return sig;
    }

    private void Rebuild()
    {
        _signature = Signature();
        foreach (var c in _grid.GetChildren()) c.QueueFree();
        _shown.Clear();
        foreach (var r in Residents())
        {
            _shown.Add(r);
            var npc = r;
            var button = new Button { CustomMinimumSize = new Vector2(52, 52), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center };
            if (PortraitIcons.TownNpc(r.Def) is { } tex) button.Icon = tex;
            else button.Text = r.Def.DisplayName[..System.Math.Min(3, r.Def.DisplayName.Length)];
            string where = r.Home is { } home ? $"lives at {home.X}, {home.Y}, {home.Z}" : "homeless";
            button.TooltipText = $"{r.Title} — {(r.Alive ? where : "dead")}\nClick, then click a room to move in. Right-click to evict.";
            if (!r.Alive || r.Home == null) button.Modulate = new Color(0.55f, 0.55f, 0.55f, 0.85f);
            button.Pressed += () => SetMode(Mode.Assign, npc);
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) Evict(npc);
            };
            _grid.AddChild(button);
        }
        if (_shown.Count == 0) _grid.AddChild(new Label { Text = "No one lives in town yet." });
    }

    private void SetMode(Mode mode, TownNpc? npc)
    {
        _mode = mode;
        _assigning = npc;
        _catcher.Visible = mode != Mode.None && IsOpen;
        _hint.Text = mode switch
        {
            Mode.Query => "Click a room to check it (right-click to cancel).",
            Mode.Assign => $"Click a room for {npc!.Title} (right-click to cancel).",
            _ => "Click a banner to move a resident, '?' to check a room.",
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry) return;
        if (e.IsActionPressed("housing"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsOpen) return;
        if (e.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    private void OnCatcherInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } click || _mode == Mode.None) return;
        _catcher.AcceptEvent();
        if (click.ButtonIndex == MouseButton.Right)
        {
            SetMode(Mode.None, null);
            return;
        }
        if (click.ButtonIndex != MouseButton.Left) return;
        if (WorldPicker.AirCellUnderMouse(GetViewport(), _ui.Game.Session.World) is not { } cell)
        {
            _ui.Notify("Point at a room.", MessageColor);
            return;
        }
        if (_mode == Mode.Query) Query(cell);
        else if (_assigning != null) Assign(_assigning, cell);
        SetMode(Mode.None, null);
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        if (_anchored && _anchor is { } a && !a.IsVisibleInTree())
        {
            Close(); // the inventory closed under it
            return;
        }
        PortraitIcons.Pump();
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 1.0;
        // Arrivals, deaths, respawns and moves change the banners; rebuild only when something did.
        if (Signature() != _signature) Rebuild();
    }
}
