using Godot;

namespace Strata.Game.UI;

public static partial class UiTheme
{
    /// <summary>The borderless text-button variation (title-screen style): white text that turns gold under the mouse.</summary>
    public const string TextButton = "TextButton";

    private static readonly Color Black = new(0f, 0f, 0f, 1f);
    private static readonly Color Well = Color.Color8(20, 26, 60, 170);
    private static readonly Color Grabber = Color.Color8(120, 140, 220);
    private static readonly Color GrabberHighlight = Color.Color8(160, 180, 255);
    private static readonly Color TabIdle = Color.Color8(35, 45, 100, 190);

    /// <summary>
    /// Builds the theme for every control type the game uses: panels, labels, buttons (and the <see cref="TextButton"/>
    /// variation), toggles (the toggle_on/off sprites), dropdowns and popups, tabs, sliders (a gold knob sprite),
    /// scrollbars, progress bars, text fields, rich text and tooltips. All text is white with a black outline in the UI
    /// font.
    /// </summary>
    private static Theme Build()
    {
        var t = new Theme { DefaultFont = UiFonts.Bold, DefaultFontSize = UiFonts.Body };
        var panel = Panel();
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "Panel", panel);
        TextColors(t, "Label");
        BuildButtons(t);
        BuildBars(t);
        BuildTabs(t);
        BuildFields(t);
        return t;
    }

    private static void TextColors(Theme t, string type, int outline = UiFonts.Outline)
    {
        t.SetColor("font_color", type, Text);
        t.SetColor("font_outline_color", type, Black);
        t.SetConstant("outline_size", type, outline);
    }

    private static void States(Theme t, string type, StyleBox normal, StyleBox hover, StyleBox pressed, StyleBox disabled)
    {
        var none = new StyleBoxEmpty();
        t.SetStylebox("normal", type, normal);
        t.SetStylebox("hover", type, hover);
        t.SetStylebox("pressed", type, pressed);
        t.SetStylebox("hover_pressed", type, pressed);
        t.SetStylebox("disabled", type, disabled);
        t.SetStylebox("focus", type, none);
    }

    private static void ButtonColors(Theme t, string type, Color hover, Color pressed)
    {
        TextColors(t, type);
        t.SetColor("font_hover_color", type, hover);
        t.SetColor("font_focus_color", type, Text);
        t.SetColor("font_pressed_color", type, pressed);
        t.SetColor("font_hover_pressed_color", type, pressed);
        t.SetColor("font_disabled_color", type, new Color(Text, 0.4f));
    }

    private static void BuildButtons(Theme t)
    {
        States(t, "Button",
            Box(PanelBlue, PanelBorder, 2, 8, 12, 5),
            Box(PanelHover, PanelBorder, 2, 8, 12, 5),
            Box(PanelHover.Darkened(0.15f), Gold, 2, 8, 12, 5),
            Box(new Color(PanelBlue, PanelBlue.A * 0.4f), new Color(PanelBorder, 0.4f), 2, 8, 12, 5));
        ButtonColors(t, "Button", Text, Gold);

        t.SetTypeVariation(TextButton, "Button");
        var bare = new StyleBoxEmpty();
        States(t, TextButton, bare, bare, bare, bare);
        ButtonColors(t, TextButton, Gold, Gold.Darkened(0.2f));

        // Toggles draw their switch or box icon on bare text, not on a framed button; switches are the pixel toggle sprites.
        var toggle = new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 };
        foreach (var type in new[] { "CheckButton", "CheckBox" })
        {
            States(t, type, toggle, toggle, toggle, toggle);
            ButtonColors(t, type, Gold, Text);
        }
        Texture2D on = UiSprites.Get("toggle_on"), off = UiSprites.Get("toggle_off");
        foreach (var (name, icon) in new[] { ("checked", on), ("unchecked", off), ("checked_disabled", on), ("unchecked_disabled", off),
                     ("checked_mirrored", on), ("unchecked_mirrored", off), ("checked_disabled_mirrored", on), ("unchecked_disabled_mirrored", off) })
            t.SetIcon(name, "CheckButton", icon);
        ButtonColors(t, "OptionButton", Text, Gold);

        var popup = Box(new Color(PanelBlue, 0.95f), PanelBorder, 2, 8, 6, 6);
        t.SetStylebox("panel", "PopupMenu", popup);
        t.SetStylebox("hover", "PopupMenu", Box(PanelHover, new Color(1f, 1f, 1f, 0.3f), 1, 6, 6, 2));
        TextColors(t, "PopupMenu");
        t.SetColor("font_hover_color", "PopupMenu", Gold);
        t.SetColor("font_disabled_color", "PopupMenu", new Color(Text, 0.4f));
    }

    private static void BuildBars(Theme t)
    {
        foreach (var (type, vertical) in new[] { ("VScrollBar", true), ("HScrollBar", false) })
        {
            StyleBoxFlat Bar(Color c) => Box(c, Colors.Transparent, 0, 6, vertical ? 6 : 0, vertical ? 0 : 6);
            t.SetStylebox("scroll", type, Bar(Well));
            t.SetStylebox("scroll_focus", type, Bar(Well));
            t.SetStylebox("grabber", type, Bar(Grabber));
            t.SetStylebox("grabber_highlight", type, Bar(GrabberHighlight));
            t.SetStylebox("grabber_pressed", type, Bar(Gold));
        }

        t.SetStylebox("background", "ProgressBar", Box(Well, PanelBorder, 2, 6, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Grabber, Colors.Transparent, 0, 6, 0, 0));
        TextColors(t, "ProgressBar");

        t.SetStylebox("slider", "HSlider", Box(Well, PanelBorder, 2, 4, 0, 3));
        t.SetStylebox("grabber_area", "HSlider", Box(Grabber, Colors.Transparent, 0, 4, 0, 3));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(GrabberHighlight, Colors.Transparent, 0, 4, 0, 3));
        t.SetIcon("grabber", "HSlider", UiSprites.Get("slider_grabber"));
        t.SetIcon("grabber_highlight", "HSlider", UiSprites.Get("slider_grabber_hover"));
        t.SetIcon("grabber_disabled", "HSlider", UiSprites.Get("slider_grabber"));
    }

    private static void BuildTabs(Theme t)
    {
        StyleBoxFlat Tab(Color fill)
        {
            var tab = Box(fill, PanelBorder, 2, 8, 14, 5);
            tab.CornerRadiusBottomLeft = tab.CornerRadiusBottomRight = 0;
            return tab;
        }
        foreach (var type in new[] { "TabContainer", "TabBar" })
        {
            t.SetStylebox("tab_selected", type, Tab(new Color(PanelHover, 0.9f)));
            t.SetStylebox("tab_unselected", type, Tab(TabIdle));
            t.SetStylebox("tab_hovered", type, Tab(PanelHover));
            t.SetStylebox("tab_disabled", type, Tab(new Color(TabIdle, 0.4f)));
            t.SetStylebox("tab_focus", type, new StyleBoxEmpty());
            t.SetColor("font_selected_color", type, Text);
            t.SetColor("font_unselected_color", type, Muted);
            t.SetColor("font_hovered_color", type, Gold);
            t.SetColor("font_disabled_color", type, new Color(Muted, 0.4f));
            t.SetColor("font_outline_color", type, Black);
            t.SetConstant("outline_size", type, UiFonts.Outline);
        }
        t.SetStylebox("panel", "TabContainer", Box(new Color(PanelBlue, 0.45f), PanelBorder, 2, 8, 6, 6));
    }

    private static void BuildFields(Theme t)
    {
        t.SetStylebox("normal", "LineEdit", Box(Well, PanelBorder, 2, 6, 8, 4));
        t.SetStylebox("read_only", "LineEdit", Box(new Color(Well, 0.4f), PanelBorder, 2, 6, 8, 4));
        var focus = Box(Colors.Transparent, Gold, 2, 6, 8, 4);
        focus.DrawCenter = false;
        t.SetStylebox("focus", "LineEdit", focus);
        TextColors(t, "LineEdit", 3);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(Muted, 0.6f));
        t.SetColor("caret_color", "LineEdit", Text);
        t.SetColor("selection_color", "LineEdit", new Color(Gold, 0.35f));

        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetColor("font_outline_color", "RichTextLabel", Black);
        t.SetConstant("outline_size", "RichTextLabel", UiFonts.Outline);

        t.SetStylebox("panel", "TooltipPanel", Box(new Color(ChatPanel, 0.92f), PanelBorder, 2, 8, 10, 6));
        TextColors(t, "TooltipLabel");
    }
}
