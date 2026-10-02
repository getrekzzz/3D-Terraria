using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's boss bar in the layout's BossBar region (bottom-centre): the boss's name over a framed red bar with its
/// head icon on the left and "life/max" on it. A hit leaves a pale chunk of the lost life that holds a moment, then
/// slides down to the real value. It fades in when the boss arrives and goes when the group falls or leaves. Paired
/// bosses and flagged parts share the one bar (<see cref="BossBarModel"/>). Drawn in one <see cref="_Draw"/>, redrawn
/// only while something on it changes.
/// </summary>
public sealed partial class BossBar : Control
{
    private const int TitleSize = 18, NumberSize = 14;
    private const float TitleHeight = 22, Icon = 40, IconGap = 4, BarHeight = 22, Frame = 2, Highlight = 3;
    private const double TrailHold = 0.4, FadeIn = 0.5;

    private static readonly Color Back = Color.Color8(30, 10, 20, 200);
    private static readonly Color Fill = Color.Color8(200, 30, 50);
    private static readonly Color Shine = Color.Color8(255, 120, 120, 160);
    private static readonly Color Chunk = Color.Color8(255, 200, 200, 180);

    private EnemyDef? _lead;
    private string _title = "", _numbers = "";
    private Texture2D? _icon;
    private long _life = -1, _max = -1;
    private double _trail, _hold;

    public BossBar()
    {
        Name = "BossBar";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>A boss is shown (the layout reserves the BossBar region while it is; the <c>ui.bossbar</c> probe).</summary>
    public bool Active => _lead != null;

    /// <summary>The bar's life over the group's maximum (0–1), and the trailing chunk's.</summary>
    public float Fraction => _max > 0 ? (float)_life / _max : 0f;

    public float TrailFraction => _max > 0 ? (float)(_trail / _max) : 0f;

    /// <summary>Follows the living bosses each frame; <paramref name="state"/> is null when none is up.</summary>
    public void Refresh(BossBarState? state, double delta)
    {
        if (state is not { } s)
        {
            _lead = null;
            return;
        }
        if (s.Lead != _lead)
        {
            // A new boss (or the other of a pair now leads): no trail from the last one, and the bar fades in.
            if (_lead == null) Modulate = new Color(1, 1, 1, 0);
            _lead = s.Lead;
            _title = s.Title;
            _icon = PortraitIcons.Boss(s.Lead);
            _trail = s.Life;
            _hold = 0;
            _life = -1;
            QueueRedraw();
        }
        if (s.Life != _life || s.Max != _max)
        {
            if (s.Life < _life) _hold = TrailHold; // each hit restarts the hold
            _life = s.Life;
            _max = s.Max;
            _numbers = $"{s.Life}/{s.Max}";
            QueueRedraw();
        }
        if (_trail < _life)
            _trail = _life; // healing never leaves a chunk
        else if (_trail > _life)
        {
            if (_hold > 0)
                _hold -= delta;
            else
                _trail = Mathf.MoveToward(_trail, _life, System.Math.Max(_max * 0.25, (_trail - _life) * 4) * delta);
            QueueRedraw();
        }
        if (Modulate.A < 1f) Modulate = new Color(1, 1, 1, Mathf.Min(1f, Modulate.A + (float)(delta / FadeIn)));
    }

    public override void _Draw()
    {
        if (_lead == null) return;
        var font = UiFonts.Bold;
        float w = Size.X;
        // The name, centred over the bar.
        var bar = new Rect2(Icon + IconGap, TitleHeight + (Icon - BarHeight) / 2, Mathf.Max(0, w - Icon - IconGap), BarHeight);
        var titleBase = new Vector2(bar.Position.X, Mathf.Round((TitleHeight + font.GetAscent(TitleSize) - font.GetDescent(TitleSize)) / 2));
        DrawStringOutline(font, titleBase, _title, HorizontalAlignment.Center, bar.Size.X, TitleSize, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, titleBase, _title, HorizontalAlignment.Center, bar.Size.X, TitleSize, UiTheme.Text);

        // The head icon on a dark disc (a disc with the initial if the image is missing).
        var iconRect = new Rect2(0, TitleHeight, Icon, Icon);
        DrawCircle(iconRect.GetCenter(), Icon / 2, Back);
        if (_icon != null)
            DrawTextureRect(_icon, iconRect, false);
        else
        {
            DrawCircle(iconRect.GetCenter(), Icon / 2 - Frame, Fill.Darkened(0.3f));
            string initial = _title.Length > 0 ? _title.Substring(_title.StartsWith("The ") ? 4 : 0, 1) : "?";
            var ib = new Vector2(0, iconRect.Position.Y + Mathf.Round((Icon + font.GetAscent(TitleSize) - font.GetDescent(TitleSize)) / 2));
            DrawString(font, ib, initial, HorizontalAlignment.Center, Icon, TitleSize, UiTheme.Text);
        }

        // Frame, background, the damage chunk, the fill and its highlight.
        DrawRect(bar.Grow(Frame), Colors.Black);
        DrawRect(bar, Back);
        float trail = Mathf.Clamp(TrailFraction, 0, 1), fill = Mathf.Clamp(Fraction, 0, 1);
        if (trail > fill) DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * trail, bar.Size.Y)), Chunk);
        if (fill > 0)
        {
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, bar.Size.Y)), Fill);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, Highlight)), Shine);
        }
        var numBase = new Vector2(bar.Position.X, bar.Position.Y + Mathf.Round((BarHeight + font.GetAscent(NumberSize) - font.GetDescent(NumberSize)) / 2));
        DrawStringOutline(font, numBase, _numbers, HorizontalAlignment.Center, bar.Size.X, NumberSize, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, numBase, _numbers, HorizontalAlignment.Center, bar.Size.X, NumberSize, UiTheme.Text);
    }
}
