using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Events;

namespace Strata.Game.UI;

/// <summary>
/// The invasion progress panel in the layout's EventBar region (bottom-right), as in Terraria: the event's icon and name
/// over a bar of the kill goal with "NN%" on it, for the latest invasion going (one with a kill goal: the Goblin Army,
/// the Pirates, the Frost Legion). When it ends the panel holds its last value (100% when won) for a few seconds, then
/// fades out. Drawn in one <see cref="_Draw"/>, redrawn only when the percentage changes.
/// </summary>
public sealed partial class EventProgressPanel : Control
{
    private const int NameSize = 18, PercentSize = 14, IconSize = 32;
    private const float Pad = 10, BarHeight = 18, Frame = 2;
    private const double Linger = 3, FadeOut = 1;

    private static readonly Color BarBack = Color.Color8(20, 20, 40, 220);
    private static readonly Color BarFill = Color.Color8(255, 196, 60);
    private static readonly Color BarShine = Color.Color8(255, 236, 160, 160);

    private readonly StyleBoxFlat _panel = new()
    {
        BgColor = UiTheme.PanelBlue, BorderColor = UiTheme.PanelBorder, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2,
        BorderWidthBottom = 2, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
    };

    private ActiveEvent? _shown;
    private EventDef? _def;
    private Texture2D? _icon;
    private int _percent = -1;
    private string _percentText = "";
    private double _ended = -1; // seconds since the shown invasion ended; −1 while it runs

    public EventProgressPanel(EventDirector events)
    {
        Name = "EventBar";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        events.Ended += def =>
        {
            if (def != _def || _ended >= 0) return;
            _ended = 0;
            if (_shown != null) Show(Mathf.FloorToInt(events.Progress(_shown) * 100f)); // 100% when won
        };
    }

    /// <summary>An invasion's progress is on show (the layout reserves the EventBar region while it is; the <c>ui.eventbar</c> probe).</summary>
    public bool Active => _def != null;

    /// <summary>The percentage on the bar.</summary>
    public int Percent => _percent;

    public void Refresh(EventDirector events, double delta)
    {
        var latest = events.LatestInvasion;
        if (latest != null && latest != _shown)
        {
            _shown = latest;
            _def = latest.Def;
            _icon = StatusIcons.Get(latest.Def);
            _ended = -1;
            _percent = -1;
            Modulate = Colors.White;
            QueueRedraw();
        }
        if (_def == null) return;
        if (_ended < 0)
        {
            Show(Mathf.FloorToInt(events.Progress(_shown!) * 100f));
            return;
        }
        _ended += delta;
        Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(1 - (_ended - Linger) / FadeOut, 0, 1));
        if (_ended < Linger + FadeOut) return;
        _shown = null;
        _def = null;
    }

    private void Show(int percent)
    {
        if (percent == _percent) return;
        _percent = percent;
        _percentText = $"{percent}%";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_def == null) return;
        var font = UiFonts.Bold;
        DrawStyleBox(_panel, new Rect2(Vector2.Zero, Size));
        if (_icon != null) DrawTextureRect(_icon, new Rect2(Pad, Pad, IconSize, IconSize), false);
        float nameX = Pad + IconSize + 8;
        var nameBase = new Vector2(nameX, Pad + Mathf.Round((IconSize + font.GetAscent(NameSize) - font.GetDescent(NameSize)) / 2));
        DrawStringOutline(font, nameBase, _def.DisplayName, HorizontalAlignment.Left, Size.X - nameX - Pad, NameSize, UiFonts.Outline,
            UiFonts.OutlineColor);
        DrawString(font, nameBase, _def.DisplayName, HorizontalAlignment.Left, Size.X - nameX - Pad, NameSize, UiTheme.Text);

        var bar = new Rect2(Pad, Size.Y - Pad - BarHeight, Size.X - 2 * Pad, BarHeight);
        DrawRect(bar.Grow(Frame), Colors.Black);
        DrawRect(bar, BarBack);
        float fill = Mathf.Clamp(_percent / 100f, 0, 1);
        if (fill > 0)
        {
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, bar.Size.Y)), BarFill);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, 3)), BarShine);
        }
        var pctBase = new Vector2(bar.Position.X, bar.Position.Y + Mathf.Round((BarHeight + font.GetAscent(PercentSize) - font.GetDescent(PercentSize)) / 2));
        DrawStringOutline(font, pctBase, _percentText, HorizontalAlignment.Center, bar.Size.X, PercentSize, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, pctBase, _percentText, HorizontalAlignment.Center, bar.Size.X, PercentSize, UiTheme.Text);
    }
}
