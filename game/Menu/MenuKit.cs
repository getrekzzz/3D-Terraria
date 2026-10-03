using System;
using System.Collections.Generic;
using Godot;
using Strata.Game.UI;

namespace Strata.Game.Menu;

/// <summary>
/// Building blocks for the menus (title, character and world screens, settings, pause): big text buttons that light up
/// on hover, framed panels, labelled option rows, sliders, toggles, choices, colour and text fields.
/// </summary>
public static class MenuKit
{
    public static readonly Color Gold = new(1f, 0.86f, 0.3f);
    public static readonly Color Idle = new(0.9f, 0.92f, 1f);

    /// <summary>A large borderless text button (title-screen style): white, gold and slightly larger under the mouse.</summary>
    public static Button Big(string text, Action pressed, int size = 30)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.All, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", Idle);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_focus_color", Gold);
        b.AddThemeColorOverride("font_pressed_color", Gold.Darkened(0.2f));
        b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        b.AddThemeConstantOverride("outline_size", 6);
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(state, empty);
        b.Pressed += () => { Audio.Sfx.PlayUi("ui_click"); pressed(); };
        b.MouseEntered += () => Audio.Sfx.PlayUi("ui_hover");
        return b;
    }

    /// <summary>A regular framed button.</summary>
    public static Button Small(string text, Action pressed, int minWidth = 140)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 38), Theme = UiTheme.Theme };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += () => { Audio.Sfx.PlayUi("ui_click"); pressed(); };
        return b;
    }

    public static Label Title(string text, int size = 56)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        l.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.08f, 0.12f, 0.95f));
        l.AddThemeConstantOverride("outline_size", 12);
        return l;
    }

    public static Label Text(string text, int size = 18, Color? color = null)
    {
        var l = new Label { Text = text, Theme = UiTheme.Theme };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    /// <summary>A dark framed panel with a vertical content box.</summary>
    public static (PanelContainer Panel, VBoxContainer Box) Panel(Vector2 minSize, int separation = 10)
    {
        var panel = new PanelContainer { CustomMinimumSize = minSize, Theme = UiTheme.Theme };
        var style = UiTheme.Panel(0.95f);
        style.ContentMarginLeft = style.ContentMarginRight = 22;
        style.ContentMarginTop = style.ContentMarginBottom = 18;
        panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        panel.AddChild(box);
        return (panel, box);
    }

    /// <summary>A screen-filling container that centres its content.</summary>
    public static CenterContainer Centered(Control content)
    {
        var c = new CenterContainer();
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        c.AddChild(content);
        return c;
    }

    /// <summary>"Label ........ field" row.</summary>
    public static HBoxContainer Row(string label, Control field, string? tooltip = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var l = Text(label, 17);
        l.CustomMinimumSize = new Vector2(250, 0);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (tooltip != null) l.TooltipText = row.TooltipText = tooltip;
        l.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(l);
        field.CustomMinimumSize = new Vector2(Mathf.Max(field.CustomMinimumSize.X, 260), 32);
        row.AddChild(field);
        return row;
    }

    public static OptionButton Choice(IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        var o = new OptionButton { Theme = UiTheme.Theme };
        foreach (var option in options) o.AddItem(option);
        o.Selected = Mathf.Clamp(selected, 0, options.Count - 1);
        o.ItemSelected += i => changed((int)i);
        return o;
    }

    public static CheckButton Toggle(bool on, Action<bool> changed)
    {
        var t = new CheckButton { ButtonPressed = on, Theme = UiTheme.Theme };
        t.Toggled += v => changed(v);
        return t;
    }

    /// <summary>A slider with its value printed beside it (<paramref name="format"/> turns the value into text).</summary>
    public static HBoxContainer Slider(double min, double max, double step, double value, Action<double> changed, Func<double, string>? format = null)
    {
        format ??= v => v.ToString("0.##");
        var box = new HBoxContainer();
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(200, 24), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var label = Text(format(value), 16);
        label.CustomMinimumSize = new Vector2(64, 0);
        s.ValueChanged += v =>
        {
            label.Text = format(v);
            changed(v);
        };
        box.AddChild(s);
        box.AddChild(label);
        return box;
    }

    public static LineEdit Field(string text, string placeholder, Action<string> changed, int maxLength = 24)
    {
        var e = new LineEdit { Text = text, PlaceholderText = placeholder, MaxLength = maxLength, Theme = UiTheme.Theme };
        e.TextChanged += t => changed(t);
        e.FocusEntered += () => InputGate.TextEntry = true;
        e.FocusExited += () => InputGate.TextEntry = false;
        return e;
    }

    public static ColorPickerButton ColorField(string hex, Action<string> changed)
    {
        var c = new ColorPickerButton { Color = Color.FromHtml(hex), EditAlpha = false, CustomMinimumSize = new Vector2(120, 30) };
        c.ColorChanged += col => changed("#" + col.ToHtml(false));
        return c;
    }

    /// <summary>A row of preset colour swatches plus a picker for anything else.</summary>
    public static HBoxContainer Swatches(IReadOnlyList<string> presets, string current, Action<string> changed)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var picker = ColorField(current, changed);
        picker.CustomMinimumSize = new Vector2(44, 30);
        foreach (var hex in presets)
        {
            var swatch = new Button { CustomMinimumSize = new Vector2(26, 26), TooltipText = hex };
            var style = new StyleBoxFlat { BgColor = Color.FromHtml(hex), BorderColor = new Color(0, 0, 0, 0.6f), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) swatch.AddThemeStyleboxOverride(state, style);
            string h = hex;
            swatch.Pressed += () =>
            {
                picker.Color = Color.FromHtml(h);
                changed(h);
            };
            box.AddChild(swatch);
        }
        box.AddChild(picker);
        return box;
    }

    public static Control Spacer(float height) => new Control { CustomMinimumSize = new Vector2(0, height) };
}
