using System;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Crafting;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>Small building blocks of the book's pages: section titles, link rows and recipe cards.</summary>
public static class GuidePage
{
    public static Label Section(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 20);
        l.AddThemeColorOverride("font_color", UiTheme.Gold);
        return l;
    }

    public static Label Text(string text, Color? color = null, int size = 16)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    /// <summary>A flat row that jumps somewhere when clicked (a mouse-stop control, so its tooltip shows).</summary>
    public static Button Link(string text, Texture2D? icon, Action? onClick, string? tooltip = null)
    {
        var b = new Button
        {
            Text = text, Icon = icon, Flat = true, Alignment = HorizontalAlignment.Left, ExpandIcon = false,
            FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip ?? "", Disabled = onClick == null,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        b.AddThemeConstantOverride("icon_max_width", 28);
        if (onClick != null) b.Pressed += onClick;
        return b;
    }

    /// <summary>
    /// A recipe: the output with its count, a row of ingredient icons with counts (each clickable, tooltip = name),
    /// "Required objects: …" and any conditions. Clicking an item calls <paramref name="open"/>.
    /// </summary>
    public static Control RecipeCard(RecipeView r, Action<ItemDef> open)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.Panel(0.6f));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var output = Link(r.Count > 1 ? $"{r.Output.DisplayName} ({r.Count})" : r.Output.DisplayName, ItemIcons.Get(r.Output), () => open(r.Output));
        output.AddThemeColorOverride("font_color", UiTheme.Rarity(r.Output.Rarity));
        box.AddChild(output);
        var row = new HFlowContainer();
        foreach (var ing in r.Ingredients)
        {
            var icon = ing.Icon;
            var b = new Button
            {
                Text = $"×{ing.Count}", Icon = ItemIcons.Get(icon), Flat = true, ExpandIcon = false,
                FocusMode = Control.FocusModeEnum.None, TooltipText = ing.Label,
            };
            b.AddThemeConstantOverride("icon_max_width", 28);
            b.Pressed += () => open(icon);
            row.AddChild(b);
        }
        box.AddChild(row);
        if (r.StationLine.Length > 0) box.AddChild(Text(r.StationLine, UiTheme.Muted, 14));
        if (r.Conditions.Length > 0) box.AddChild(Text("Only " + r.Conditions, UiTheme.Muted, 14));
        card.AddChild(box);
        return card;
    }
}
