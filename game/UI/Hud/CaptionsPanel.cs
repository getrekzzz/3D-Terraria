using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Audio;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// Subtitles (X-12, the Subtitles setting): a caption line above the chat log for each important sound heard (bosses,
/// residents arriving, rain and thunder, nearby creatures; data/config/captions.json), Minecraft-style: one dark row per
/// line, newest at the bottom, in its kind's colour (through the colour-blind palette), with an arrow turned toward a
/// positional sound as the camera moves, fading out a few seconds after the sound was last heard. Lines come from
/// <see cref="Sfx.Heard"/>; <see cref="CaptionFeed"/> keeps them. Placed by the HUD (<see cref="HudOverlays.CaptionsRect"/>);
/// draws only while lines show.
/// </summary>
public sealed partial class CaptionsPanel : Control
{
    private const float PadX = 8f;
    private const float Arrow = 6f;
    private const float ArrowGap = 6f;
    private const int FontSize = UiFonts.Body;

    private readonly CaptionFeed _feed;
    private readonly Dictionary<string, Color> _kindColors = new();
    private readonly Dictionary<string, float> _widths = new();
    private readonly float?[] _bearings;
    private readonly Vector2[] _triangle = new Vector2[3];
    private double _now;
    private bool _placed;

    public CaptionsPanel(CaptionsConfig config)
    {
        Name = "Captions";
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        Visible = false;
        _feed = new CaptionFeed(config);
        _bearings = new float?[System.Math.Max(1, config.MaxLines)];
        foreach (var (kind, hex) in config.Colors) _kindColors[kind] = Color.FromHtml(hex);
    }

    /// <summary>The lines kept (also while the setting is off: none).</summary>
    public CaptionFeed Feed => _feed;

    /// <summary>Lines on screen now (the <c>ui.captions</c> probe).</summary>
    public int LinesShown => Visible ? System.Math.Min(_feed.Count, Rows) : 0;

    /// <summary>Lines on screen with a direction arrow (the <c>ui.caption_arrows</c> probe).</summary>
    public int ArrowsShown { get; private set; }

    /// <summary>Lines on screen of <paramref name="kind"/> ("weather", "boss"…; the <c>ui.captions_weather</c> probe).</summary>
    public int LinesOfKind(string kind)
    {
        int n = 0;
        for (int i = _feed.Count - LinesShown; i < _feed.Count; i++)
            if (_feed[i].Kind == kind) n++;
        return n;
    }

    /// <summary>The newest line's words, or "".</summary>
    public string LastLine => _feed.Count > 0 ? _feed[_feed.Count - 1].Text : "";

    private int Rows => (int)(Size.Y / HudOverlays.CaptionLine + 0.01f);

    public override void _EnterTree() => Sfx.Heard += OnHeard;

    public override void _ExitTree() => Sfx.Heard -= OnHeard;

    /// <summary>Takes <paramref name="rect"/> (from the layout); hidden without one.</summary>
    public void Place(UiRect? rect)
    {
        _placed = rect.HasValue;
        if (rect is not { } r) return;
        Position = new Vector2(r.X, r.Y);
        Size = new Vector2(r.W, r.H);
    }

    private void OnHeard(string id, Vector3? at)
    {
        if (!UserSettings.Current.Subtitles) return;
        if (at is not { } source)
        {
            _feed.Hear(id, _now);
            return;
        }
        var listener = GetViewport()?.GetCamera3D() is { } camera ? camera.GlobalPosition : source;
        _feed.Hear(id, _now, Num(source), Num(listener));
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (!UserSettings.Current.Subtitles) _feed.Clear();
        _feed.Expire(_now);
        bool show = _placed && _feed.Count > 0;
        if (Visible != show) Visible = show;
        if (!show)
        {
            ArrowsShown = 0;
            return;
        }
        // Arrows follow the camera: where each positional sound lies from the current view.
        var camera = GetViewport().GetCamera3D();
        int arrows = 0;
        for (int i = 0; i < _feed.Count; i++)
        {
            var line = _feed[i];
            _bearings[i] = line.Positioned && camera != null
                ? CaptionFeed.Bearing(Num(camera.GlobalPosition), Num(-camera.GlobalBasis.Z), line.Source)
                : null;
            if (_bearings[i] != null && i >= _feed.Count - Rows) arrows++;
        }
        ArrowsShown = arrows;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = UiFonts.Bold;
        float line = HudOverlays.CaptionLine;
        int first = System.Math.Max(0, _feed.Count - Rows);
        float y = Size.Y - (_feed.Count - first) * line;
        float ascent = font.GetAscent(FontSize), descent = font.GetDescent(FontSize);
        for (int i = first; i < _feed.Count; i++, y += line)
        {
            var caption = _feed[i];
            float alpha = _feed.Alpha(i, _now);
            if (alpha <= 0f) continue;
            float? bearing = _bearings[i];
            float arrowSpace = bearing != null ? 2 * Arrow + ArrowGap : 0f;
            float width = Mathf.Min(Size.X, 2 * PadX + arrowSpace + TextWidth(font, caption.Text));
            DrawRect(new Rect2(0, y + 1, width, line - 2), new Color(0f, 0f, 0f, 0.55f * alpha));
            var color = _kindColors.TryGetValue(caption.Kind, out var c) ? UiTheme.Vision(c) : UiTheme.Text;
            color.A = alpha;
            if (bearing is { } b) DrawArrow(new Vector2(PadX + Arrow, y + line / 2), b, color);
            var at = new Vector2(PadX + arrowSpace, y + Mathf.Round((line + ascent - descent) / 2));
            DrawStringOutline(font, at, caption.Text, HorizontalAlignment.Left, -1, FontSize, UiFonts.Outline, new Color(UiFonts.OutlineColor, alpha));
            DrawString(font, at, caption.Text, HorizontalAlignment.Left, -1, FontSize, color);
        }
    }

    /// <summary>A small triangle pointing toward the sound: up is ahead, right is to the right, down is behind.</summary>
    private void DrawArrow(Vector2 centre, float bearing, Color color)
    {
        var dir = new Vector2(Mathf.Sin(bearing), -Mathf.Cos(bearing));
        var side = new Vector2(-dir.Y, dir.X);
        for (int pass = 0; pass < 2; pass++)
        {
            float r = pass == 0 ? Arrow + 1.5f : Arrow;
            _triangle[0] = centre + dir * r;
            _triangle[1] = centre - dir * (r * 0.7f) + side * (r * 0.8f);
            _triangle[2] = centre - dir * (r * 0.7f) - side * (r * 0.8f);
            DrawColoredPolygon(_triangle, pass == 0 ? new Color(0f, 0f, 0f, color.A) : color);
        }
    }

    private float TextWidth(Font font, string text)
    {
        if (_widths.TryGetValue(text, out float w)) return w;
        return _widths[text] = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
    }

    private static System.Numerics.Vector3 Num(Vector3 v) => new(v.X, v.Y, v.Z);
}
