using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Map;

namespace Strata.Game.UI;

/// <summary>A point of interest drawn over the map: a position in metres (= map pixels), a colour and an optional label.</summary>
public readonly record struct MapMarker(Vector2 At, Color Color, string? Label = null, float? Yaw = null, float Size = 5f);

/// <summary>
/// Draws an <see cref="ExploredMap"/>: each 64×64 tile becomes a cached texture (rebuilt when the tile changes), shown
/// crisp at any zoom. The surface view shades land by its height like a relief map; the underground view shows cave
/// floors bright, the rock between them dark and the surface above faintly. Unexplored ground stays black. Used by
/// the full-screen map (<see cref="Interactive"/>: drag to pan, wheel to zoom) and the minimap.
/// </summary>
public sealed partial class MapView : Control
{
    private const int TileBuildsPerFrame = 12;
    private static readonly Color Unexplored = new(0.035f, 0.04f, 0.055f);

    private readonly Dictionary<(int Key, bool Underground), (int Version, ImageTexture Texture)> _textures = new();
    private Color[] _blockColors = Array.Empty<Color>();
    private Color[] _liquidColors = Array.Empty<Color>();
    private bool _dragging;
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

    public Func<IEnumerable<MapMarker>>? Markers { get; set; }

    public void Initialize(ContentDatabase content)
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
        _blockColors = new Color[content.Blocks.Count];
        for (int i = 0; i < _blockColors.Length; i++) _blockColors[i] = Color.FromHtml(content.Blocks[i].Color);
        _liquidColors = new Color[8];
        foreach (var liquid in content.Liquids.All)
            if (liquid.Code is >= 0 and < 8) _liquidColors[liquid.Code] = Color.FromHtml(liquid.Color);
    }

    public override void _Process(double delta) => QueueRedraw();

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
        if (Markers != null)
            foreach (var m in Markers()) DrawMarker(m);
    }

    private void DrawMarker(MapMarker m)
    {
        var p = ToScreen(m.At);
        if (m.Yaw is { } yaw)
        {
            // An arrow pointing where the player looks (map +Z is down the screen).
            var dir = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var side = new Vector2(-dir.Y, dir.X);
            var tip = p + dir * m.Size * 1.6f;
            var arrow = new[] { tip, p - dir * m.Size + side * m.Size, p - dir * m.Size * 0.4f, p - dir * m.Size - side * m.Size };
            DrawColoredPolygon(arrow, m.Color);
            DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, Colors.Black, 1.5f);
        }
        else
        {
            DrawCircle(p, m.Size + 1.5f, Colors.Black);
            DrawCircle(p, m.Size, m.Color);
        }
        if (m.Label != null && Zoom >= 1.5f)
        {
            var font = UiFonts.Bold; // the theme's face, also on the minimap's own layer
            var at = p + new Vector2(m.Size + 4, 5);
            DrawStringOutline(font, at, m.Label, HorizontalAlignment.Left, -1, UiFonts.Small, UiFonts.Outline, UiFonts.OutlineColor);
            DrawString(font, at, m.Label, HorizontalAlignment.Left, -1, UiFonts.Small, Colors.White);
        }
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
            case InputEventMouseMotion motion when _dragging:
                Center -= motion.Relative / Zoom;
                _dragged += motion.Relative.Length();
                AcceptEvent();
                break;
        }
    }
}
