using System;
using System.Collections.Generic;
using Godot;
using Strata.Game.UI;

namespace Strata.Game.Menu;

/// <summary>
/// Building blocks for the menus (title, character and world screens, settings, pause): big text buttons that turn gold
/// and grow on hover, pixel icon buttons, blue list rows, framed panels, labelled option rows, sliders, toggles,
/// choices, colour and text fields. Everything else comes from the global theme (UiTheme), so no control sets its own.
/// </summary>
public static class MenuKit
{
    public static readonly Color Gold = new(1f, 0.86f, 0.3f);
    public static readonly Color Idle = new(0.9f, 0.92f, 1f);

    /// <summary>
    /// A large borderless text button (title-screen style, also the pause menu's): white, turning gold and growing to
    /// <see cref="HoverScale"/> under the mouse.
    /// </summary>
    public static Button Big(string text, Action pressed, int size = 30)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.All, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontOverride("font", UiFonts.Bold);
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", Idle);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_focus_color", Gold);
        b.AddThemeColorOverride("font_pressed_color", Gold.Darkened(0.2f));
        b.AddThemeColorOverride("font_hover_pressed_color", Gold.Darkened(0.2f));
        b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        b.AddThemeConstantOverride("outline_size", 6);
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(state, empty);
        b.Pressed += () => { Audio.Sfx.PlayUi("ui_click"); pressed(); };
        b.MouseEntered += () => Audio.Sfx.PlayUi("ui_hover");
        HoverGrow(b);
        return b;
    }

    /// <summary>The size a <see cref="Big"/> button or an icon grows to under the mouse.</summary>
    public const float HoverScale = 1.08f;

    private const double HoverSeconds = 0.08;

    /// <summary>
    /// Makes <paramref name="control"/> grow to <see cref="HoverScale"/> about its centre under the mouse (a short tween)
    /// and shrink back when it leaves. Scaling leaves the layout alone, so nothing around it moves.
    /// </summary>
    public static void HoverGrow(Control control)
    {
        Tween? tween = null;
        void To(float scale)
        {
            tween?.Kill();
            if (!control.IsInsideTree()) return;
            control.PivotOffset = control.Size / 2;
            tween = control.CreateTween();
            tween.TweenProperty(control, "scale", new Vector2(scale, scale), HoverSeconds);
        }
        control.Resized += () => control.PivotOffset = control.Size / 2;
        control.MouseEntered += () => To(HoverScale);
        control.MouseExited += () => To(1f);
    }

    /// <summary>
    /// A 32 px pixel-sprite button (the select screens' play, favourite and delete icons): <paramref name="sprite"/> from
    /// assets/ui, brighter and growing under the mouse (brighter with the controller's focus too), with
    /// <paramref name="tooltip"/> as its hover text.
    /// </summary>
    public static TextureButton Icon(string sprite, string tooltip, Action pressed)
    {
        var b = new TextureButton
        {
            TextureNormal = UiSprites.Get(sprite), TooltipText = tooltip, CustomMinimumSize = new Vector2(32, 32),
            StretchMode = TextureButton.StretchModeEnum.KeepCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, FocusMode = Control.FocusModeEnum.All,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        b.MouseEntered += () =>
        {
            b.Modulate = new Color(1.25f, 1.25f, 1.25f);
            Audio.Sfx.PlayUi("ui_hover");
        };
        b.MouseExited += () => b.Modulate = b.HasFocus() ? b.Modulate : Colors.White;
        // A controller reaches it by focus (the D-pad): it brightens as under the mouse.
        b.FocusEntered += () => b.Modulate = new Color(1.25f, 1.25f, 1.25f);
        b.FocusExited += () => b.Modulate = Colors.White;
        b.Pressed += () => { Audio.Sfx.PlayUi("ui_click"); pressed(); };
        HoverGrow(b);
        return b;
    }

    /// <summary>
    /// A row of a select list (a character or a world): a blue panel that lights up while the mouse is over it or any
    /// of its buttons, with a horizontal content box.
    /// </summary>
    public static (PanelContainer Panel, HBoxContainer Row) RowPanel()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        var idle = UiTheme.Panel();
        idle.ContentMarginLeft = idle.ContentMarginRight = 12;
        idle.ContentMarginTop = idle.ContentMarginBottom = 8;
        var hover = (StyleBoxFlat)idle.Duplicate();
        hover.BgColor = UiTheme.PanelHover;
        hover.BorderColor = UiTheme.Gold.Darkened(0.35f);
        panel.AddThemeStyleboxOverride("panel", idle);
        panel.MouseEntered += () => panel.AddThemeStyleboxOverride("panel", hover);
        panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", idle);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 14);
        panel.AddChild(row);
        return (panel, row);
    }

    /// <summary>A regular framed button.</summary>
    public static Button Small(string text, Action pressed, int minWidth = 140)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 38) };
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
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    /// <summary>A blue framed panel (Terraria's) with a vertical content box.</summary>
    public static (PanelContainer Panel, VBoxContainer Box) Panel(Vector2 minSize, int separation = 10)
    {
        var panel = new PanelContainer { CustomMinimumSize = minSize };
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
        var o = new OptionButton();
        foreach (var option in options) o.AddItem(option);
        o.Selected = Mathf.Clamp(selected, 0, options.Count - 1);
        o.ItemSelected += i => changed((int)i);
        return o;
    }

    public static CheckButton Toggle(bool on, Action<bool> changed)
    {
        var t = new CheckButton { ButtonPressed = on };
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
        var e = new LineEdit { Text = text, PlaceholderText = placeholder, MaxLength = maxLength };
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
