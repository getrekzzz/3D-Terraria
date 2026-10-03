using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Map;

namespace Strata.Game.UI;

/// <summary>
/// Draws an <see cref="ExploredMap"/>: each 64×64 tile becomes a cached texture (rebuilt when the tile changes), shown
/// crisp at any zoom. The surface view shades land by its height like a relief map; the underground view shows cave
/// floors bright, the rock between them dark and the surface above faintly. Unexplored ground stays black. Used by
/// the full-screen map (<see cref="Interactive"/>: drag to pan, wheel to zoom, hover a marker for its name) and the
/// minimap; the <see cref="Markers"/> are drawn over it by a <see cref="MapMarkerLayer"/>.
/// </summary>
public sealed partial class MapView : Control
{
    private const int TileBuildsPerFrame = 12;
    private static readonly Color Unexplored = new(0.035f, 0.04f, 0.055f);

    private readonly Dictionary<(int Key, bool Underground), (int Version, ImageTexture Texture)> _textures = new();
    private Color[] _blockColors = Array.Empty<Color>();
    private Color[] _liquidColors = Array.Empty<Color>();
    private bool _dragging;
    private MapMarkerLayer _markerLayer = null!;
    private float _dragged;

    /// <summary>A left click that did not drag the map, at a map position (pixels of <see cref="Map"/>).</summary>
    public event Action<Vector2>? Clicked;

    public ExploredMap? Map { get; set; }

    /// <summary>Show the caves (at the depths they were explored) instead of the surface.</summary>
    public bool Underground { get; set; }

    /// <summary>Map pixel at the centre of the view.</summary>
    public Vector2 Center { get; set; }

    /// <summary>Screen pixels per map pixel (1 map pixel = 1 m).</summary>
    public float Zoom { get; set; } = 2f;

    public bool Interactive { get; set; }

    /// <summary>Fills the list it is given with the markers to draw this frame (the list is reused).</summary>
    public Action<List<MapMarker>>? Markers { get; set; }

    /// <summary>The pointer is over the map (an interactive map only follows it).</summary>
    public bool HasMouse { get; private set; }

    /// <summary>Where the pointer is over the map, in the view's pixels.</summary>
    public Vector2 Mouse { get; private set; }

    /// <summary>The name of the marker under the pointer (full map), or null.</summary>
    public string? HoveredMarker => _markerLayer.Hovered;

    public void Initialize(ContentDatabase content)
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
        AddChild(_markerLayer = new MapMarkerLayer(this));
        if (Interactive) MouseExited += () => HasMouse = false;
        _blockColors = new Color[content.Blocks.Count];
        for (int i = 0; i < _blockColors.Length; i++) _blockColors[i] = Color.FromHtml(content.Blocks[i].Color);
        _liquidColors = new Color[8];
        foreach (var liquid in content.Liquids.All)
            if (liquid.Code is >= 0 and < 8) _liquidColors[liquid.Code] = Color.FromHtml(liquid.Color);
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
        _markerLayer.QueueRedraw();
    }

    /// <summary>
    /// The share (0–1) of the view showing discovered ground in its current mode (the <c>map.minimap_coverage</c>
    /// probe); reads every map pixel in view, so it is for probes, not frames.
    /// </summary>
    public float ExploredShare()
    {
        if (Map == null || Size.X <= 0 || Size.Y <= 0) return 0f;
        var min = ToMap(Vector2.Zero);
        var max = ToMap(Size);
        int x0 = (int)MathF.Floor(min.X), x1 = (int)MathF.Ceiling(max.X), z0 = (int)MathF.Floor(min.Y), z1 = (int)MathF.Ceiling(max.Y);
        int seen = 0, total = 0;
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                total++;
                if (x < 0 || z < 0 || x >= Map.Width || z >= Map.Depth) continue;
                if ((Underground ? Map.Cave(x, z) : Map.Surface(x, z)) != MapValue.Unexplored) seen++;
            }
        return total == 0 ? 0f : (float)seen / total;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Unexplored);
        if (Map == null) return;
        var min = ToMap(Vector2.Zero);
        var max = ToMap(Size);
        int t0x = Math.Max(0, (int)MathF.Floor(min.X / ExploredMap.TileSize)), t1x = Math.Min((Map.Width - 1) / ExploredMap.TileSize, (int)MathF.Floor(max.X / ExploredMap.TileSize));
        int t0z = Math.Max(0, (int)MathF.Floor(min.Y / ExploredMap.TileSize)), t1z = Math.Min((Map.Depth - 1) / ExploredMap.TileSize, (int)MathF.Floor(max.Y / ExploredMap.TileSize));
        int builds = TileBuildsPerFrame;
        for (int tz = t0z; tz <= t1z; tz++)
            for (int tx = t0x; tx <= t1x; tx++)
            {
                if (Map.TileAt(tx, tz) is not { } tile) continue;
                var texture = Texture(tile, ref builds);
                if (texture == null) continue;
                var at = ToScreen(new Vector2(tx, tz) * ExploredMap.TileSize);
                DrawTextureRect(texture, new Rect2(at, Vector2.One * ExploredMap.TileSize * Zoom), false);
            }
        // The world's edge.
        var edge = new Rect2(ToScreen(Vector2.Zero), new Vector2(Map.Width, Map.Depth) * Zoom);
        DrawRect(edge, new Color(0.5f, 0.55f, 0.65f, 0.5f), false, 1.5f);
    }

    private ImageTexture? Texture(MapTile tile, ref int builds)
    {
        var key = (ExploredMap.TileKey(tile.X, tile.Z), Underground);
        bool have = _textures.TryGetValue(key, out var cached);
        if (have && cached.Version == tile.Version) return cached.Texture;
        if (builds-- <= 0) return have ? cached.Texture : null; // spread the work over frames
        var image = Image.CreateFromData(ExploredMap.TileSize, ExploredMap.TileSize, false, Image.Format.Rgba8, Pixels(tile));
        var texture = have ? cached.Texture : ImageTexture.CreateFromImage(image);
        if (have) texture.Update(image);
        _textures[key] = (tile.Version, texture);
        return texture;
    }

    private byte[] Pixels(MapTile tile)
    {
        const int n = ExploredMap.TileSize;
        var data = new byte[n * n * 4];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                var c = Underground ? CaveColor(tile, i) : SurfaceColor(tile, i, x, z);
                int o = i * 4;
                data[o] = (byte)(Mathf.Clamp(c.R, 0f, 1f) * 255f);
                data[o + 1] = (byte)(Mathf.Clamp(c.G, 0f, 1f) * 255f);
                data[o + 2] = (byte)(Mathf.Clamp(c.B, 0f, 1f) * 255f);
                data[o + 3] = 255;
            }
        return data;
    }

    private Color SurfaceColor(MapTile tile, int i, int x, int z)
    {
        ushort v = tile.Surface[i];
        if (v == MapValue.Unexplored) return Unexplored;
        var c = ColorOf(v);
        if (MapValue.IsLiquid(v)) return c;
        // Relief: lit from the north-west, so slopes read like a shaded topographic map.
        int n = x > 0 && z > 0 ? i - ExploredMap.TileSize - 1 : i;
        float slope = tile.Surface[n] == MapValue.Unexplored ? 0f : tile.SurfaceY[i] - tile.SurfaceY[n];
        return c * Mathf.Clamp(1f + slope * 0.07f, 0.72f, 1.3f);
    }

    private Color CaveColor(MapTile tile, int i)
    {
        ushort v = tile.Cave[i];
        if (v == MapValue.Unexplored)
            return tile.Surface[i] == MapValue.Unexplored ? Unexplored : Unexplored.Lerp(ColorOf(tile.Surface[i]), 0.22f); // the land above, faintly
        var c = ColorOf(v);
        return MapValue.IsWall(v) ? c * 0.38f : c;
    }

    private Color ColorOf(ushort v)
    {
        if (MapValue.IsLiquid(v)) return _liquidColors[(int)MapValue.LiquidOf(v) & 7];
        int id = MapValue.BlockId(v);
        return id >= 0 && id < _blockColors.Length ? _blockColors[id] : Colors.Magenta;
    }

    public Vector2 ToScreen(Vector2 map) => Size * 0.5f + (map - Center) * Zoom;

    public Vector2 ToMap(Vector2 screen) => Center + (screen - Size * 0.5f) / Zoom;

    public override void _GuiInput(InputEvent e)
    {
        if (!Interactive) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                // Zoom about the mouse: the map pixel under the cursor stays put.
                var before = ToMap(wheel.Position);
                Zoom = Mathf.Clamp(Zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f), 0.25f, 12f);
                Center += before - ToMap(wheel.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) _dragged = 0f;
                else if (_dragging && _dragged < 4f) Clicked?.Invoke(ToMap(button.Position));
                _dragging = button.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                HasMouse = true;
                Mouse = motion.Position;
                if (!_dragging) break;
                Center -= motion.Relative / Zoom;
                _dragged += motion.Relative.Length();
                AcceptEvent();
                break;
        }
    }
}
