using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's chat log in the layout's Chat region (bottom-left; right of the crafting column while the inventory is
/// open): world messages in their colours ("Eye of Cthulhu has awoken!" purple, arrivals blue, deaths red, events
/// green), newest at the bottom, each fading out 10 s after it arrived (<see cref="ChatHistory"/>). The chat key (Enter)
/// opens the history: every kept line (the last 50) at full strength on a translucent backdrop, scrolled with the wheel,
/// until Enter or Esc. It is an overlay with no text input, so the player keeps moving. One pooled label per kept line,
/// built once; a frame only recomputes the fade of the lines on show.
/// </summary>
public sealed partial class ChatLog : Control
{
    private const string Gate = "chat";

    private readonly ChatHistory _history = new();
    private readonly Label[] _labels = new Label[ChatHistory.Capacity];
    private readonly Panel _backdrop;
    private readonly ScrollContainer _scroll;
    private double _now;
    private int _shownFrom = -1, _shownAdded = -1; // the first line the labels show and the history they were built from
    private int _vision; // the colour-blind palette the lines are tinted for (X-12)
    private bool _open;

    public ChatLog()
    {
        Name = "ChatLog";
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        _backdrop = new Panel { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        _backdrop.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(UiTheme.ChatPanel, 0.55f) });
        AddChild(_backdrop);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_scroll);
        var lines = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End, // newest at the bottom, short logs sit on the region's floor
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        lines.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(lines);
        for (int i = 0; i < _labels.Length; i++)
        {
            var label = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", UiFonts.Body);
            label.AddThemeColorOverride("font_color", UiTheme.Text); // white, tinted by the message colour (the outline stays black)
            label.AddThemeColorOverride("font_outline_color", UiFonts.OutlineColor);
            label.AddThemeConstantOverride("outline_size", UiFonts.Outline);
            lines.AddChild(label);
            _labels[i] = label;
        }
        // The newest line stays in view: the content grew (a message, a wrapped line) or the history opened.
        _scroll.GetVScrollBar().Changed += () => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }

    /// <summary>The history view is open.</summary>
    public bool IsOpen => _open;

    /// <summary>Lines on show now (the <c>ui.chat_lines</c> probe): every kept line in the history, else those not faded.</summary>
    public int LinesShown => _history.Count - (_open ? 0 : _history.FirstShown(_now));

    /// <summary>The newest line's text, or "".</summary>
    public string LastLine => _history.Count > 0 ? _history[_history.Count - 1].Text : "";

    /// <summary>Adds a message at the bottom of the log in <paramref name="color"/>.</summary>
    public void Add(string text, Color color)
    {
        _history.Add(text, new Rgb((byte)color.R8, (byte)color.G8, (byte)color.B8), _now);
        if (_open) Show(0);
    }

    public void SetOpen(bool open)
    {
        if (open == _open) return;
        _open = open;
        _backdrop.Visible = open;
        _scroll.VerticalScrollMode = open ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.ShowNever;
        _scroll.MouseFilter = open ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore; // the wheel scrolls the history
        InputGate.SetOpen(Gate, open, GateKind.Overlay);
        if (open) Show(0);
        else _shownFrom = -1; // the fading log is rebuilt on the next frame
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // A text field (the console, a chest's name) takes its own Enter first; menus and the map stop the chat key.
        if (InputGate.TextEntry || (!_open && InputGate.GameplayBlocked)) return;
        if (e.IsActionPressed("chat") || (_open && e.IsActionPressed("pause")))
        {
            SetOpen(!_open);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (_vision != UiTheme.VisionVersion)
        {
            _vision = UiTheme.VisionVersion; // the colour-blind mode changed: recolour the lines
            _shownAdded = -1;
            if (_open) Show(0);
        }
        if (_open) return; // the history shows every line at full strength
        int from = _history.FirstShown(_now);
        if (from != _shownFrom || _history.Added != _shownAdded) Show(from);
        for (int i = from; i < _history.Count; i++)
        {
            float alpha = ChatHistory.Alpha(_now - _history[i].Time);
            var label = _labels[i];
            if (!Mathf.IsEqualApprox(label.Modulate.A, alpha)) label.Modulate = new Color(1, 1, 1, alpha);
        }
    }

    /// <summary>Points the labels at the kept lines from <paramref name="from"/> on (label i shows line i) and hides the rest.</summary>
    private void Show(int from)
    {
        _shownFrom = from;
        _shownAdded = _history.Added;
        for (int i = 0; i < _labels.Length; i++)
        {
            var label = _labels[i];
            bool shown = i >= from && i < _history.Count;
            label.Visible = shown;
            if (!shown) continue;
            var line = _history[i];
            label.Text = line.Text;
            label.SelfModulate = UiTheme.Vision(Color.Color8(line.Color.R, line.Color.G, line.Color.B));
            if (_open) label.Modulate = Colors.White;
        }
    }
}
