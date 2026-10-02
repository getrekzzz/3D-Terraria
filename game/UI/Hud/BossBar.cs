using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's boss bars in the layout's BossBar region (bottom-centre): one bar per boss group alive at once, stacked
/// upward from the bottom, up to <see cref="BossBarModel.MaxBars"/> (the mechanical bosses together, two event
/// minibosses). Each is the boss's name over a framed red bar with its head icon on the left and "life/max" on it; a
/// hit leaves a pale chunk of the lost life that holds a moment, then slides down to the real value. A bar fades in
/// when its group arrives and fades out over half a second when the group falls or leaves; the others keep their
/// places. Paired bosses and flagged parts share a bar (<see cref="BossBarModel"/>). Drawn in one <see cref="_Draw"/>,
/// redrawn only while something on it changes; the bars' state objects are pooled.
/// </summary>
public sealed partial class BossBar : Control
{
    private const int TitleSize = 18, NumberSize = 14;
    private const float TitleHeight = 22, Icon = 40, IconGap = 4, BarHeight = 22, Frame = 2, Highlight = 3;
    private const double TrailHold = 0.4, FadeIn = 0.5, FadeOut = 0.5;

    private static readonly Color Back = Color.Color8(30, 10, 20, 200);
    private static readonly Color Fill = Color.Color8(200, 30, 50);
    private static readonly Color Shine = Color.Color8(255, 120, 120, 160);
    private static readonly Color Chunk = Color.Color8(255, 200, 200, 180);

    /// <summary>One bar on screen: its group's look and life, the trailing chunk and its fade.</summary>
    private sealed class Row
    {
        public object? Group;
        public EnemyDef? Lead;
        public string Title = "", Numbers = "";
        public Texture2D? Icon;
        public long Life = -1, Max = -1;
        public double Trail, Hold;
        public float Alpha;
        public bool Live, Matched;

        public float Fraction => Max > 0 ? (float)Life / Max : 0f;

        public float TrailFraction => Max > 0 ? (float)(Trail / Max) : 0f;
    }

    private readonly List<Row> _rows = new(); // the bottom bar first
    private readonly Stack<Row> _pool = new();

    public BossBar()
    {
        Name = "BossBar";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>A boss bar is shown, or still fading out (the layout reserves the region; the <c>ui.bossbar</c> probe).</summary>
    public bool Active => _rows.Count > 0;

    /// <summary>Bars on screen, fading ones included (the region's height; the <c>ui.bossbars</c> probe).</summary>
    public int Count => _rows.Count;

    /// <summary>The bottom living bar's life over its group's maximum (0–1), and the trailing chunk's.</summary>
    public float Fraction => _rows.Count > 0 ? First().Fraction : 0f;

    public float TrailFraction => _rows.Count > 0 ? First().TrailFraction : 0f;

    /// <summary>Follows the living boss groups (one <see cref="BossBarState"/> each, the largest first) every frame.</summary>
    public void Refresh(List<BossBarState> states, double delta)
    {
        foreach (var row in _rows) row.Matched = false;
        foreach (var s in states)
        {
            var row = Find(s.Group);
            if (row == null)
            {
                if (_rows.Count >= BossBarModel.MaxBars && !DropFading()) continue;
                row = _pool.Count > 0 ? _pool.Pop() : new Row();
                row.Group = s.Group;
                row.Lead = null;
                row.Alpha = 0f; // fades in
                _rows.Add(row);
            }
            row.Matched = true;
            row.Live = true;
            Update(row, s, delta);
        }
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var row = _rows[i];
            if (!row.Matched) row.Live = false;
            if (row.Live)
            {
                if (row.Alpha < 1f)
                {
                    row.Alpha = Mathf.Min(1f, row.Alpha + (float)(delta / FadeIn));
                    QueueRedraw();
                }
                continue;
            }
            // Its group fell or left: the bar fades out where it is.
            row.Alpha -= (float)(delta / FadeOut);
            QueueRedraw();
            if (row.Alpha > 0f) continue;
            _rows.RemoveAt(i);
            _pool.Push(row);
        }
    }

    public override void _Draw()
    {
        // As many bars as the region holds (a crowded screen may give room for fewer than are up), from the bottom.
        int fit = Mathf.Min(_rows.Count, (int)((Size.Y + UiLayout.Gap + 0.5f) / (UiLayout.BossBarHeight + UiLayout.Gap)));
        for (int i = 0; i < fit; i++)
            DrawRow(_rows[i], Size.Y - (i + 1) * UiLayout.BossBarHeight - i * UiLayout.Gap);
    }

    /// <summary>The first bar still alive (else the bottom one).</summary>
    private Row First()
    {
        foreach (var row in _rows)
            if (row.Live) return row;
        return _rows[0];
    }

    private Row? Find(object? group)
    {
        foreach (var row in _rows)
            if (!row.Matched && Equals(row.Group, group)) return row;
        return null;
    }

    /// <summary>Makes room for a new bar by dropping one already fading out; false when every bar is alive.</summary>
    private bool DropFading()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Live) continue;
            _pool.Push(_rows[i]);
            _rows.RemoveAt(i);
            return true;
        }
        return false;
    }

    private void Update(Row row, BossBarState s, double delta)
    {
        if (s.Lead != row.Lead)
        {
            // A new group (or the other of a pair now leads): no trail from the last one.
            row.Lead = s.Lead;
            row.Title = s.Title;
            row.Icon = PortraitIcons.Boss(s.Lead);
            row.Trail = s.Life;
            row.Hold = 0;
            row.Life = -1;
            QueueRedraw();
        }
        if (s.Life != row.Life || s.Max != row.Max)
        {
            if (s.Life < row.Life) row.Hold = TrailHold; // each hit restarts the hold
            row.Life = s.Life;
            row.Max = s.Max;
            row.Numbers = $"{s.Life}/{s.Max}";
            QueueRedraw();
        }
        if (row.Trail < row.Life)
            row.Trail = row.Life; // healing never leaves a chunk
        else if (row.Trail > row.Life)
        {
            if (row.Hold > 0)
                row.Hold -= delta;
            else
                row.Trail = Mathf.MoveToward(row.Trail, row.Life, System.Math.Max(row.Max * 0.25, (row.Trail - row.Life) * 4) * delta);
            QueueRedraw();
        }
    }

    /// <summary>One bar with its top at <paramref name="top"/>: the name over it, the head icon left of it.</summary>
    private void DrawRow(Row row, float top)
    {
        var font = UiFonts.Bold;
        float a = row.Alpha, w = Size.X;
        var text = Faded(UiTheme.Text, a);
        var outline = Faded(UiFonts.OutlineColor, a);
        // The name, centred over the bar.
        var bar = new Rect2(Icon + IconGap, top + TitleHeight + (Icon - BarHeight) / 2, Mathf.Max(0, w - Icon - IconGap), BarHeight);
        var titleBase = new Vector2(bar.Position.X, top + Mathf.Round((TitleHeight + font.GetAscent(TitleSize) - font.GetDescent(TitleSize)) / 2));
        DrawStringOutline(font, titleBase, row.Title, HorizontalAlignment.Center, bar.Size.X, TitleSize, UiFonts.Outline, outline);
        DrawString(font, titleBase, row.Title, HorizontalAlignment.Center, bar.Size.X, TitleSize, text);

        // The head icon on a dark disc (a disc with the initial if the image is missing).
        var iconRect = new Rect2(0, top + TitleHeight, Icon, Icon);
        DrawCircle(iconRect.GetCenter(), Icon / 2, Faded(Back, a));
        if (row.Icon != null)
            DrawTextureRect(row.Icon, iconRect, false, new Color(1, 1, 1, a));
        else
        {
            DrawCircle(iconRect.GetCenter(), Icon / 2 - Frame, Faded(Fill.Darkened(0.3f), a));
            string initial = row.Title.Length > 0 ? row.Title.Substring(row.Title.StartsWith("The ") ? 4 : 0, 1) : "?";
            var ib = new Vector2(0, iconRect.Position.Y + Mathf.Round((Icon + font.GetAscent(TitleSize) - font.GetDescent(TitleSize)) / 2));
            DrawString(font, ib, initial, HorizontalAlignment.Center, Icon, TitleSize, text);
        }

        // Frame, background, the damage chunk, the fill and its highlight.
        DrawRect(bar.Grow(Frame), Faded(Colors.Black, a));
        DrawRect(bar, Faded(Back, a));
        float trail = Mathf.Clamp(row.TrailFraction, 0, 1), fill = Mathf.Clamp(row.Fraction, 0, 1);
        if (trail > fill) DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * trail, bar.Size.Y)), Faded(Chunk, a));
        if (fill > 0)
        {
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, bar.Size.Y)), Faded(Fill, a));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, Highlight)), Faded(Shine, a));
        }
        var numBase = new Vector2(bar.Position.X, bar.Position.Y + Mathf.Round((BarHeight + font.GetAscent(NumberSize) - font.GetDescent(NumberSize)) / 2));
        DrawStringOutline(font, numBase, row.Numbers, HorizontalAlignment.Center, bar.Size.X, NumberSize, UiFonts.Outline, outline);
        DrawString(font, numBase, row.Numbers, HorizontalAlignment.Center, bar.Size.X, NumberSize, text);
    }

    private static Color Faded(Color c, float alpha) => new(c.R, c.G, c.B, c.A * alpha);
}
