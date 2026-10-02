using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Map;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The full-screen world map (M), like the reference game's: everything this character has discovered in this world,
/// dragged to pan and scrolled to zoom. U switches between the surface and the caves (it opens on whichever the player
/// is in), C re-centres on the player. Markers: the player (arrow), the world spawn, their bed and the town's residents.
/// </summary>
public sealed partial class WorldMapScreen : CanvasLayer
{
    private const string Gate = "map";
    private UiContext _ui = null!;
    private Control _root = null!;
    private MapView _view = null!;
    private Label _title = null!;

    public bool IsOpen => _root.Visible;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "WorldMap";
        Layer = 30;
        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _view = new MapView { Interactive = true, Zoom = 3f };
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _view.Initialize(ui.Game.Session.Content);
        _view.Markers = () => Markers(ui);
        _root.AddChild(_view);
        _title = new Label { Position = new Vector2(24, 18), Theme = UiTheme.Theme };
        _title.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(_title);
        var help = new Label
        {
            Text = "Drag to move · wheel to zoom · U surface / caves · C centre on you · M or Esc to close",
            Theme = UiTheme.Theme,
        };
        help.AddThemeFontSizeOverride("font_size", 15);
        help.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        help.Position = new Vector2(24, -40);
        _root.AddChild(help);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry) return;
        if (e.IsActionPressed("map") || (IsOpen && e.IsActionPressed("pause")))
        {
            SetOpen(!IsOpen);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsOpen || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.U)
        {
            _view.Underground = !_view.Underground;
            UpdateTitle();
        }
        else if (key.Keycode == Key.C) CenterOnPlayer();
        else return;
        GetViewport().SetInputAsHandled();
    }

    public void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        if (open)
        {
            _ui.Game.Maps.Reveal(_ui.Entity, force: true);
            _view.Map = _ui.Game.Maps.For(_ui.Entity);
            _view.Underground = !_ui.Game.Maps.IsOutdoors(_ui.Entity);
            CenterOnPlayer();
            UpdateTitle();
        }
        _root.Visible = open;
        Audio.Sfx.Play(open ? "ui_open" : "ui_close");
        InputGate.SetOpen(Gate, open, GateKind.Modal);
    }

    private void CenterOnPlayer()
    {
        var p = _ui.Entity.Position;
        _view.Center = new Vector2(p.X, p.Z) / (Units.CellSize * ExploredMap.CellsPerPixel);
    }

    private void UpdateTitle()
    {
        var map = _view.Map;
        int explored = map?.CountExplored() ?? 0;
        float share = map == null ? 0f : explored * 100f / (map.Width * (float)map.Depth);
        _title.Text = $"{_ui.Game.Session.Settings.Name} — {(_view.Underground ? "Caves" : "Surface")}   ({share:0.0}% explored)";
    }

    /// <summary>What the map marks: the player, the world spawn, the player's bed here and the living residents.</summary>
    public static IEnumerable<MapMarker> Markers(UiContext ui)
    {
        const float perPixel = Units.CellSize * ExploredMap.CellsPerPixel;
        var session = ui.Game.Session;
        var spawn = session.SpawnCell;
        yield return new MapMarker(new Vector2(spawn.X, spawn.Z) / ExploredMap.CellsPerPixel, new Color(0.35f, 0.9f, 0.4f), "Spawn", Size: 4f);
        if (ui.Entity.State.SpawnPoints.TryGetValue(session.Settings.Name, out var bed) && bed.Length == 3)
            yield return new MapMarker(new Vector2(bed[0], bed[2]) / ExploredMap.CellsPerPixel, new Color(0.4f, 0.65f, 1f), "Bed", Size: 4f);
        foreach (var r in ui.Game.Town.Residents)
            if (r.Alive) yield return new MapMarker(new Vector2(r.Position.X, r.Position.Z) / perPixel, new Color(1f, 0.92f, 0.75f), r.Title, Size: 3.5f);
        var p = ui.Entity.Position;
        var aim = ui.Entity.Aim;
        yield return new MapMarker(new Vector2(p.X, p.Z) / perPixel, new Color(1f, 0.85f, 0.2f), null, Mathf.Atan2(aim.X, aim.Z), 6f);
    }
}

/// <summary>
/// The minimap under the hearts (the layout's Minimap region): the discovered map around the player, following them
/// (surface or caves by where they stand), in a gold <c>minimap_frame</c> with + and − zoom buttons in its corner that
/// take clicks while the mouse is free. Hidden while the full map is open, when the inventory needs its corner for the
/// equipment column, or when switched off in Settings → Interface.
/// </summary>
public sealed partial class Minimap : CanvasLayer
{
    private const float MinZoom = 0.5f, MaxZoom = 6f, ZoomStep = 1.25f;
    private const float Inset = 6; // the map runs under the frame's inner dark ring
    private const float Button = 18;
    private UiContext _ui = null!;
    private WorldMapScreen _full = null!;
    private Control _box = null!;
    private MapView _view = null!;
    private TextureButton _zoomIn = null!, _zoomOut = null!;
    private bool _placed;
    private bool _mouseFree;

    public void Initialize(UiContext ui, WorldMapScreen full)
    {
        _ui = ui;
        _full = full;
        Name = "Minimap";
        Layer = 10; // above the empty-screen click catcher (9), so the zoom buttons take clicks with the inventory open
        _box = LayoutWidgets.Register(new Control { Name = "Minimap", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore });
        AddChild(_box);
        _view = new MapView { Zoom = 2f, MouseFilter = Control.MouseFilterEnum.Ignore };
        _view.Initialize(ui.Game.Session.Content);
        _view.Markers = () => WorldMapScreen.Markers(ui);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _view.OffsetLeft = _view.OffsetTop = Inset;
        _view.OffsetRight = _view.OffsetBottom = -Inset;
        _box.AddChild(_view);
        var frame = new NinePatchRect
        {
            Texture = UiSprites.Get("minimap_frame"), DrawCenter = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            PatchMarginLeft = 8, PatchMarginTop = 8, PatchMarginRight = 8, PatchMarginBottom = 8,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _box.AddChild(frame);
        _zoomIn = ZoomButton("zoom_in", "Zoom in", ZoomStep);
        _zoomOut = ZoomButton("zoom_out", "Zoom out", 1 / ZoomStep);
    }

    /// <summary>The map's zoom (screen pixels per map pixel), <see cref="MinZoom"/>..<see cref="MaxZoom"/>.</summary>
    public float Zoom => _view.Zoom;

    /// <summary>Takes the Minimap region of <paramref name="layout"/>; hidden when the layout leaves it out.</summary>
    public void Place(UiLayoutResult layout)
    {
        _placed = LayoutWidgets.Place(_box, layout, UiRegion.Minimap);
        float x = _box.Size.X - Inset - 2 - Button, y = _box.Size.Y - Inset - 2 - Button;
        _zoomOut.Position = new Vector2(x, y);
        _zoomIn.Position = new Vector2(x - Button - 2, y);
    }

    /// <summary>
    /// Presses the + (<paramref name="zoomIn"/>) or − button as a click would (the <c>ui minimap</c> command); false when
    /// it would not take the click (hidden, or the mouse is captured).
    /// </summary>
    public bool Press(bool zoomIn)
    {
        var button = zoomIn ? _zoomIn : _zoomOut;
        if (!button.IsVisibleInTree() || button.MouseFilter == Control.MouseFilterEnum.Ignore) return false;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    /// <summary>Multiplies the zoom by <paramref name="factor"/> within its limits.</summary>
    public void ZoomBy(float factor) => _view.Zoom = Mathf.Clamp(_view.Zoom * factor, MinZoom, MaxZoom);

    public override void _Process(double delta)
    {
        bool show = _placed && Settings.UserSettings.Current.ShowMinimap && !_full.IsOpen && _ui.Entity.Alive;
        _box.Visible = show;
        if (!show) return;
        bool free = Input.MouseMode != Input.MouseModeEnum.Captured;
        if (free != _mouseFree)
        {
            _mouseFree = free;
            _zoomIn.MouseFilter = _zoomOut.MouseFilter = free ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        }
        var maps = _ui.Game.Maps;
        _view.Map ??= maps.For(_ui.Entity);
        _view.Underground = !maps.IsOutdoors(_ui.Entity);
        var p = _ui.Entity.Position;
        _view.Center = new Vector2(p.X, p.Z) / (Units.CellSize * ExploredMap.CellsPerPixel);
    }

    private TextureButton ZoomButton(string sprite, string tooltip, float factor)
    {
        var b = new TextureButton
        {
            Name = sprite, TextureNormal = UiSprites.Get(sprite), TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Size = new Vector2(Button, Button),
        };
        b.Pressed += () =>
        {
            ZoomBy(factor);
            Audio.Sfx.Play("ui_click");
        };
        _box.AddChild(b);
        return b;
    }
}
