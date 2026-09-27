using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Map;

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
        InputGate.SetOpen(Gate, open);
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
/// The minimap in the top-right corner: the discovered map around the player, following them (surface or caves by where
/// they stand). Hidden while the full map is open or when switched off in Settings → Interface.
/// </summary>
public sealed partial class Minimap : CanvasLayer
{
    private const float Side = 210f;
    private UiContext _ui = null!;
    private WorldMapScreen _full = null!;
    private PanelContainer _frame = null!;
    private MapView _view = null!;

    public void Initialize(UiContext ui, WorldMapScreen full)
    {
        _ui = ui;
        _full = full;
        Name = "Minimap";
        Layer = 5;
        _frame = new PanelContainer { CustomMinimumSize = new Vector2(Side, Side), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = UiTheme.Panel(0.85f);
        style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 3;
        _frame.AddThemeStyleboxOverride("panel", style);
        _frame.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _frame.Position = new Vector2(-Side - 16, 70);
        AddChild(_frame);
        _view = new MapView { Zoom = 2f, MouseFilter = Control.MouseFilterEnum.Ignore };
        _view.Initialize(ui.Game.Session.Content);
        _view.Markers = () => WorldMapScreen.Markers(ui);
        _frame.AddChild(_view);
    }

    public override void _Process(double delta)
    {
        bool show = Settings.UserSettings.Current.ShowMinimap && !_full.IsOpen && _ui.Entity.Alive;
        _frame.Visible = show;
        if (!show) return;
        var maps = _ui.Game.Maps;
        _view.Map ??= maps.For(_ui.Entity);
        _view.Underground = !maps.IsOutdoors(_ui.Entity);
        var p = _ui.Entity.Position;
        _view.Center = new Vector2(p.X, p.Z) / (Units.CellSize * ExploredMap.CellsPerPixel);
    }
}
