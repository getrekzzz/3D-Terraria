using Godot;

namespace Strata.Game.UI;

/// <summary>
/// One line of the UI's white, black-outlined text drawn straight into the control's rectangle, vertically centred and
/// clipped to it. Unlike a <see cref="Label"/> it has no minimum size, so a HUD widget never grows past its layout
/// region; tint it with <see cref="CanvasItem.SelfModulate"/> (the outline stays black).
/// </summary>
public partial class OutlinedText : Control
{
    private string _text = "";
    private int _fontSize = UiFonts.Body;
    private HorizontalAlignment _align = HorizontalAlignment.Center;

    public OutlinedText()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            QueueRedraw();
        }
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            QueueRedraw();
        }
    }

    public HorizontalAlignment Align
    {
        get => _align;
        set
        {
            if (_align == value) return;
            _align = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_text.Length == 0) return;
        var font = UiFonts.Bold;
        float ascent = font.GetAscent(_fontSize), descent = font.GetDescent(_fontSize);
        var baseline = new Vector2(0, Mathf.Round((Size.Y + ascent - descent) / 2));
        DrawStringOutline(font, baseline, _text, _align, Size.X, _fontSize, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, baseline, _text, _align, Size.X, _fontSize, UiTheme.Text);
    }
}
