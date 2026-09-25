using Godot;

namespace Strata.Game.UI;

/// <summary>Shared look for all UI built in code: palette, panel and slot styles, rarity colours.</summary>
public static class UiTheme
{
    public static readonly Color PanelColor = new(0.07f, 0.09f, 0.14f, 0.86f);
    public static readonly Color PanelBorder = new(0.35f, 0.45f, 0.65f, 0.9f);
    public static readonly Color SlotColor = new(0.14f, 0.19f, 0.32f, 0.82f);
    public static readonly Color SlotBorder = new(0.32f, 0.42f, 0.66f, 0.95f);
    public static readonly Color SelectedBorder = new(1f, 0.86f, 0.35f, 1f);
    public static readonly Color Text = new(0.93f, 0.95f, 1f);
    public static readonly Color Muted = new(0.62f, 0.68f, 0.8f);
    public static readonly Color Life = new(0.86f, 0.18f, 0.22f);
    public static readonly Color Mana = new(0.22f, 0.45f, 0.95f);

    private static Theme? _theme;

    public static Theme Theme => _theme ??= Build();

    public static StyleBoxFlat Panel(float alpha = 1f) => new()
    {
        BgColor = new Color(PanelColor, PanelColor.A * alpha),
        BorderColor = PanelBorder,
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
    };

    public static StyleBoxFlat Slot(bool selected, bool hovered) => new()
    {
        BgColor = hovered ? SlotColor.Lightened(0.15f) : SlotColor,
        BorderColor = selected ? SelectedBorder : SlotBorder,
        BorderWidthLeft = selected ? 3 : 2, BorderWidthRight = selected ? 3 : 2, BorderWidthTop = selected ? 3 : 2, BorderWidthBottom = selected ? 3 : 2,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
    };

    /// <summary>Item name colour by rarity tier (white, blue, green, orange, red, pink, purple…).</summary>
    public static Color Rarity(int rarity) => rarity switch
    {
        < 0 => new Color(0.6f, 0.6f, 0.6f),
        0 => Colors.White,
        1 => new Color(0.59f, 0.59f, 1f),
        2 => new Color(0.59f, 1f, 0.59f),
        3 => new Color(1f, 0.78f, 0.59f),
        4 => new Color(1f, 0.59f, 0.59f),
        5 => new Color(1f, 0.59f, 1f),
        6 => new Color(0.82f, 0.63f, 1f),
        7 => new Color(0.59f, 1f, 0.04f),
        _ => new Color(1f, 1f, 0.04f),
    };

    private static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 15 };
        t.SetStylebox("panel", "PanelContainer", Panel());
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.85f));
        t.SetConstant("outline_size", "Label", 4);
        var button = Panel(0.9f);
        button.ContentMarginTop = button.ContentMarginBottom = 4;
        t.SetStylebox("normal", "Button", button);
        var hover = (StyleBoxFlat)button.Duplicate();
        hover.BgColor = hover.BgColor.Lightened(0.12f);
        t.SetStylebox("hover", "Button", hover);
        t.SetStylebox("pressed", "Button", hover);
        t.SetColor("font_color", "Button", Text);
        t.SetStylebox("panel", "TooltipPanel", Panel());
        return t;
    }
}
