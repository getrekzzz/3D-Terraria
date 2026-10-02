using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// The one item tooltip, as Terraria shows it: instantly, beside the pointer and following it, each line in its
/// colour (<see cref="UiTheme.TooltipColor"/>; Expert and Master names cycle). Slots call <see cref="Show"/> and
/// <see cref="Hide"/> on hover; the lines are rebuilt only when the hovered stack changes, and every quarter second
/// for text that changes on its own (prices, set bonuses). An empty slot can name itself ('Helmet', 'Dye'), and
/// <see cref="ShowText"/> shows plain lines for other controls (the defence shield). It hides when its owner hides or
/// the pointer is captured.
/// </summary>
public sealed partial class TooltipFollower : CanvasLayer
{
    public const int MaxLines = 24;
    private const double RefreshSeconds = 0.25;
    private static readonly Vector2 PointerOffset = new(16, 16);
    // Below the held stack, which sits at the pointer.
    private static readonly Vector2 HoldingOffset = new(16, 16 + CursorItemLayer.IconSize + 4);
    private const float Margin = 4;

    private readonly Label[] _labels = new Label[MaxLines];
    private Control _root = null!;
    private PanelContainer _panel = null!;
    private CursorItemLayer? _cursor;
    private Control? _owner;
    private Func<ItemStack>? _read;
    private TooltipSource? _source;
    private string? _emptyText;
    private Func<string>? _text;
    private string? _textShown;
    private ItemStack _stack;
    private double _refresh;
    private int _lines;
    private int _nameLine = -1;

    /// <summary>The tooltip in the scene (null outside a world): slots show theirs through it.</summary>
    public static TooltipFollower? Current { get; private set; }

    /// <summary>Lines shown now; 0 while hidden (the <c>ui.tooltip_lines</c> probe).</summary>
    public int LineCount => _panel.Visible ? _lines : 0;

    public void Initialize(CursorItemLayer? cursor)
    {
        _cursor = cursor;
        Name = "Tooltip";
        Layer = 95;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, ThemeTypeVariation = "TooltipPanel" };
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        for (int i = 0; i < MaxLines; i++)
        {
            // White text; each line's colour is its modulate, so recolouring allocates nothing.
            box.AddChild(_labels[i] = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore });
            _labels[i].AddThemeFontSizeOverride("font_size", UiFonts.Body);
        }
        _panel.AddChild(box);
        _root.AddChild(_panel);
    }

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
    }

    /// <summary>
    /// Shows the tooltip of the stack <paramref name="read"/> returns, for as long as <paramref name="owner"/> is hovered;
    /// while the slot is empty, <paramref name="emptyText"/> (the slot's name) if given.
    /// </summary>
    public void Show(Control owner, Func<ItemStack> read, TooltipSource source, string? emptyText = null)
    {
        _owner = owner;
        _read = read;
        _source = source;
        _emptyText = emptyText;
        _text = null;
        Rebuild(read());
        Follow();
    }

    /// <summary>
    /// Text mode: the white lines of <paramref name="text"/> (split at line breaks) for as long as <paramref name="owner"/>
    /// is hovered, rebuilt when the returned string changes (return a cached one: it is read every frame).
    /// </summary>
    public void ShowText(Control owner, Func<string> text)
    {
        _owner = owner;
        _read = null;
        _source = null;
        _emptyText = null;
        _text = text;
        RebuildText(text());
        Follow();
    }

    /// <summary>Hides the tooltip if <paramref name="owner"/> is the control it belongs to.</summary>
    public void Hide(Control owner)
    {
        if (_owner == owner) Clear();
    }

    public override void _Process(double delta)
    {
        if (_owner == null) return;
        if (!IsInstanceValid(_owner) || !_owner.IsVisibleInTree() || Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Clear();
            return;
        }
        if (_text != null)
        {
            var text = _text();
            if (!ReferenceEquals(text, _textShown) && text != _textShown) RebuildText(text);
            Follow();
            return;
        }
        var stack = _read!();
        _refresh -= delta;
        if (!stack.Equals(_stack) || _refresh <= 0) Rebuild(stack);
        if (!_panel.Visible) return;
        if (_nameLine >= 0 && UiTheme.IsAnimated(_stack.Item!.Rarity)) _labels[_nameLine].Modulate = UiTheme.Rarity(_stack.Item.Rarity);
        Follow();
    }

    private void Rebuild(ItemStack stack)
    {
        _stack = stack;
        _refresh = RefreshSeconds;
        _lines = 0;
        _nameLine = -1;
        if (stack.IsEmpty && _emptyText != null) SetLine(_emptyText, TooltipKind.Stat, 0);
        else if (!stack.IsEmpty)
        {
            int rarity = stack.Item!.Rarity;
            foreach (var (text, kind) in _source!(stack))
            {
                if (_lines == MaxLines) break;
                if (kind == TooltipKind.Name && _nameLine < 0) _nameLine = _lines;
                SetLine(text, kind, rarity);
            }
        }
        FinishLines();
    }

    private void RebuildText(string text)
    {
        _textShown = text;
        _lines = 0;
        _nameLine = -1;
        foreach (var line in text.Split('\n'))
            if (_lines < MaxLines) SetLine(line, TooltipKind.Stat, 0);
        FinishLines();
    }

    private void SetLine(string text, TooltipKind kind, int rarity)
    {
        var label = _labels[_lines++];
        if (label.Text != text) label.Text = text;
        label.Modulate = UiTheme.TooltipColor(kind, rarity);
        label.Visible = true;
    }

    private void FinishLines()
    {
        for (int i = _lines; i < MaxLines && _labels[i].Visible; i++) _labels[i].Visible = false;
        _panel.Visible = _lines > 0;
        if (_panel.Visible) _panel.ResetSize(); // shrink to the new lines
    }

    /// <summary>Beside the pointer (below the held stack), kept inside the screen.</summary>
    private void Follow()
    {
        var size = _panel.Size;
        var view = _root.Size;
        var pos = _root.GetLocalMousePosition() + (_cursor is { ShownCount: > 0 } ? HoldingOffset : PointerOffset);
        pos.X = Mathf.Max(Margin, Mathf.Min(pos.X, view.X - size.X - Margin));
        pos.Y = Mathf.Max(Margin, Mathf.Min(pos.Y, view.Y - size.Y - Margin));
        _panel.Position = pos;
    }

    private void Clear()
    {
        _owner = null;
        _read = null;
        _source = null;
        _emptyText = null;
        _text = null;
        _textShown = null;
        _stack = ItemStack.Empty;
        _panel.Visible = false;
    }
}
