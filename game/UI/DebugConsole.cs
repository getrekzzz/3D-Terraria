using System.Collections.Generic;
using Godot;
using Strata.Core.Commands;

namespace Strata.Game.UI;

/// <summary>
/// Drop-down developer console (toggle with ` or F1): runs the shared command registry, keeps history
/// (Up/Down) and completes command names with Tab.
/// </summary>
public sealed partial class DebugConsole : CanvasLayer
{
    private const string GateName = "console";
    private readonly List<string> _history = new();
    private int _historyIndex;
    private CommandRegistry _registry = null!;
    private CommandContext _ctx = null!;
    private PanelContainer _panel = null!;
    private RichTextLabel _output = null!;
    private LineEdit _input = null!;

    public void Initialize(CommandRegistry registry, CommandContext ctx)
    {
        _registry = registry;
        _ctx = ctx;
        Layer = 50;
        Name = "DebugConsole";
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.CustomMinimumSize = new Vector2(0, 320);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.04f, 0.05f, 0.07f, 0.88f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        var box = new VBoxContainer();
        _output = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, BbcodeEnabled = true, SelectionEnabled = true };
        _output.AddThemeFontSizeOverride("normal_font_size", 14);
        _input = new LineEdit { PlaceholderText = "command (help for a list)" };
        _input.TextSubmitted += Submit;
        _input.GuiInput += OnInputKey;
        box.AddChild(_output);
        box.AddChild(_input);
        _panel.AddChild(box);
        AddChild(_panel);
        Print("[color=#8fb8ff]Strata console — type 'help'.[/color]");
    }

    public bool IsOpen => _panel.Visible;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("console")) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    public void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        InputGate.SetOpen(GateName, _panel.Visible);
        InputGate.TextEntry = _panel.Visible;
        if (_panel.Visible)
        {
            _input.Clear();
            _input.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    public CommandResult Run(string line)
    {
        var result = _registry.Execute(_ctx, line);
        Print($"[color=#9aa4b4]> {Escape(line)}[/color]");
        if (result.Output.Length > 0) Print(result.Ok ? Escape(result.Output) : $"[color=#ff7a6a]{Escape(result.Output)}[/color]");
        return result;
    }

    private void Submit(string text)
    {
        _input.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;
        _history.Add(text);
        _historyIndex = _history.Count;
        Run(text);
    }

    private void OnInputKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Up when _history.Count > 0:
                _historyIndex = Mathf.Max(0, _historyIndex - 1);
                SetInput(_history[_historyIndex]);
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Mathf.Min(_history.Count, _historyIndex + 1);
                SetInput(_historyIndex < _history.Count ? _history[_historyIndex] : "");
                break;
            case Key.Tab:
                Complete();
                break;
            case Key.Quoteleft:
            case Key.F1:
                Toggle();
                break;
            default:
                return;
        }
        _input.AcceptEvent();
    }

    private void Complete()
    {
        var text = _input.Text.TrimStart();
        if (text.Contains(' ')) return;
        var matches = new List<string>();
        foreach (var c in _registry.Commands)
            if (c.Name.StartsWith(text, System.StringComparison.OrdinalIgnoreCase)) matches.Add(c.Name);
        if (matches.Count == 1) SetInput(matches[0] + " ");
        else if (matches.Count > 1) Print(string.Join("  ", matches));
    }

    private void SetInput(string text)
    {
        _input.Text = text;
        _input.CaretColumn = text.Length;
    }

    private void Print(string bbcode) => _output.AppendText(bbcode + "\n");

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
