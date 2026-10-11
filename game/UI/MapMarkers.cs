using System.Collections.Generic;
using Godot;

namespace Strata.Game.UI;

/// <summary>
/// A point of interest drawn over the map at <see cref="At"/> (metres = map pixels): an <see cref="Icon"/> of
/// <see cref="Size"/> screen pixels (a resident's head, a boss's head, the spawn flag, the bed), an arrow when it has a
/// <see cref="Yaw"/> (the player) or else a dot of radius <see cref="Size"/> in <see cref="Color"/>. <see cref="Name"/>
/// shows beside the pointer while it is hovered on the full map, never as a label on the map itself.
/// </summary>
public readonly record struct MapMarker(Vector2 At, Color Color, Texture2D? Icon = null, string? Name = null, float? Yaw = null, float Size = 5f);

/// <summary>
/// The markers of a <see cref="MapView"/>, drawn by a child layer so head icons are filtered smoothly (with their
/// mipmaps) while the map's own pixels stay crisp. On an interactive map the marker under the pointer shows its name
/// beside it, in the UI font at hover-text size, as Terraria's full map does.
/// </summary>
public sealed partial class MapMarkerLayer : Control
{
    private readonly MapView _map;
    private readonly List<MapMarker> _markers = new();
    private readonly Vector2[] _arrow = new Vector2[4];
    private readonly Vector2[] _outline = new Vector2[5];

    public MapMarkerLayer(MapView map)
    {
        _map = map;
        Name = "Markers";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The name of the marker under the pointer on an interactive map, or null.</summary>
    public string? Hovered { get; private set; }

    public override void _Draw()
    {
        _markers.Clear();
        _map.Markers?.Invoke(_markers);
        foreach (var m in _markers) DrawMarker(m);
        Hovered = null;
        if (!_map.Interactive || !_map.HasMouse) return;
        var mouse = _map.Mouse;
        for (int i = _markers.Count - 1; i >= 0; i--) // the one drawn last is on top
        {
            var m = _markers[i];
            if (m.Name == null || !Bounds(m).Grow(2).HasPoint(mouse)) continue;
            Hovered = m.Name;
            var at = (mouse + new Vector2(14, 22)).Round();
            DrawStringOutline(UiFonts.Bold, at, m.Name, HorizontalAlignment.Left, -1, UiFonts.Hover, UiFonts.Outline, UiFonts.OutlineColor);
            DrawString(UiFonts.Bold, at, m.Name, HorizontalAlignment.Left, -1, UiFonts.Hover, UiTheme.Text);
            return;
        }
    }

    /// <summary>The screen rectangle a marker covers (its icon, or the square around its dot or arrow).</summary>
    private Rect2 Bounds(MapMarker m)
    {
        var p = _map.ToScreen(m.At);
        float half = m.Icon != null ? m.Size / 2 : m.Size * 1.6f;
        return new Rect2(p - new Vector2(half, half), new Vector2(half, half) * 2);
    }

    private void DrawMarker(MapMarker m)
    {
        var p = _map.ToScreen(m.At);
        if (m.Icon != null)
        {
            DrawTextureRect(m.Icon, Bounds(m), false);
            return;
        }
        if (m.Yaw is { } yaw)
        {
            // An arrow pointing where the player looks (map +Z is down the screen).
            var dir = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var side = new Vector2(-dir.Y, dir.X);
            var tip = p + dir * m.Size * 1.6f;
            Span4(tip, p - dir * m.Size + side * m.Size, p - dir * m.Size * 0.4f, p - dir * m.Size - side * m.Size, m.Color);
            return;
        }
        DrawCircle(p, m.Size + 1.5f, Colors.Black);
        DrawCircle(p, m.Size, m.Color);
    }

    /// <summary>A filled quadrilateral with a dark outline, from reused point arrays.</summary>
    private void Span4(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        _arrow[0] = _outline[0] = _outline[4] = a;
        _arrow[1] = _outline[1] = b;
        _arrow[2] = _outline[2] = c;
        _arrow[3] = _outline[3] = d;
        DrawColoredPolygon(_arrow, color);
        DrawPolyline(_outline, Colors.Black, 1.5f);
    }
}
