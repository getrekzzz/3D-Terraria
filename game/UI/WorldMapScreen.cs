using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Map;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The full-screen world map (M), like the reference game's: everything this character has discovered in this world,
/// dragged to pan and scrolled to zoom. U switches between the surface and the caves (it opens on whichever the player
/// is in), C re-centres on the player. Markers, as on Terraria's map: the player (arrow), the world spawn's flag, their
/// bed, the head of each living resident and boss; hovering one shows its name beside the pointer.
/// </summary>
public sealed partial class WorldMapScreen : CanvasLayer
{
    private const string Gate = "map";
    public const float IconSize = 26, MinimapIconSize = 18;
    private static readonly Dictionary<Strata.Core.Gameplay.Town.TownNpc, (string Name, string Title)> Titles = new();
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
        _view.Markers = into => Markers(ui, into, IconSize);
        _root.AddChild(_view);
        _title = new Label { Position = new Vector2(24, 18) };
        _title.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(_title);
        var help = new Label { Text = "Drag to move · wheel to zoom · U surface / caves · C centre on you · M or Esc to close" };
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

    /// <summary>The name of the marker under the pointer while the map is open, or null.</summary>
    public string? HoveredMarker => IsOpen ? _view.HoveredMarker : null;

    /// <summary>The map's markers drawn as an icon rather than a dot (the <c>map.marker_icons</c> probe; allocates).</summary>
    public int MarkerIcons()
    {
        var markers = new List<MapMarker>();
        Markers(_ui, markers, IconSize);
        int icons = 0;
        foreach (var m in markers)
            if (m.Icon != null) icons++;
        return icons;
    }

    /// <summary>
    /// What the map marks, into <paramref name="into"/>, drawn in this order: the world spawn's flag, the player's bed
    /// here, the head of each living resident and boss (<paramref name="icon"/> pixels; a dot when the image is missing)
    /// and the player's arrow on top. Each carries its name for the full map's hover.
    /// </summary>
    public static void Markers(UiContext ui, List<MapMarker> into, float icon)
    {
        const float perPixel = Units.CellSize * ExploredMap.CellsPerPixel;
        var session = ui.Game.Session;
        var spawn = session.SpawnCell;
        into.Add(new MapMarker(new Vector2(spawn.X, spawn.Z) / ExploredMap.CellsPerPixel, Colors.White, UiSprites.Get("map_spawn"), "Spawn", Size: icon));
        if (ui.Entity.State.SpawnPoints.TryGetValue(session.Settings.Name, out var bed) && bed.Length == 3)
            into.Add(new MapMarker(new Vector2(bed[0], bed[2]) / ExploredMap.CellsPerPixel, Colors.White, UiSprites.Get("map_bed"), "Bed", Size: icon));
        var residents = ui.Game.Town.Residents;
        for (int i = 0; i < residents.Count; i++)
        {
            var r = residents[i];
            if (r.Alive) into.Add(Head(new Vector2(r.Position.X, r.Position.Z) / perPixel, Items.PortraitIcons.TownNpc(r.Def), Title(r), icon));
        }
        var npcs = ui.Game.Npcs.All;
        for (int i = 0; i < npcs.Count; i++)
        {
            var n = npcs[i];
            // One head per boss body (not its hands or segments), as Terraria marks bosses.
            if (!n.Alive || !n.IsBoss || n.Part != null) continue;
            into.Add(Head(new Vector2(n.Position.X, n.Position.Z) / perPixel, Items.PortraitIcons.Boss(n.Def), n.Def.Boss!.Title, icon));
        }
        var p = ui.Entity.Position;
        var aim = ui.Entity.Aim;
        into.Add(new MapMarker(new Vector2(p.X, p.Z) / perPixel, new Color(1f, 0.85f, 0.2f), null, ui.Entity.State.Name, Mathf.Atan2(aim.X, aim.Z), 6f));
    }

    /// <summary>"Andrew the Guide", built once per name rather than every frame the map draws.</summary>
    private static string Title(Strata.Core.Gameplay.Town.TownNpc r)
    {
        if (!Titles.TryGetValue(r, out var t) || !ReferenceEquals(t.Name, r.Name)) Titles[r] = t = (r.Name, r.Title);
        return t.Title;
    }

    /// <summary>A head icon, or a pale dot of the same reach when the image is missing.</summary>
    private static MapMarker Head(Vector2 at, Texture2D? head, string name, float icon) =>
        head != null ? new MapMarker(at, Colors.White, head, name, Size: icon) : new MapMarker(at, new Color(1f, 0.92f, 0.75f), null, name, Size: icon * 0.2f);
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
        _view.Markers = into => WorldMapScreen.Markers(ui, into, WorldMapScreen.MinimapIconSize);
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

    /// <summary>The share (0–1) of the minimap showing discovered ground (the <c>map.minimap_coverage</c> probe).</summary>
    public float Coverage => _view.ExploredShare();

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
