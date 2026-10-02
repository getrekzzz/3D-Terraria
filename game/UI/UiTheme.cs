using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>The kinds of item slot, each with Terraria's slot tint (inventory blue, chest teal, equip violet…).</summary>
public enum SlotKind
{
    Inventory,
    HotbarSelected,
    Chest,
    Shop,
    Equip,
    Vanity,
    Dye,
    Misc,
    Trash,
    Coin,
    Ammo,
    Favorite,
    Crafting,
    CraftingSelected,
    Locked,
}

/// <summary>
/// The faithful-Terraria look shared by every screen: see-through royal-blue panels, slot tints by kind, white text with a
/// black outline, rarity, coin and chat colours. <see cref="Theme"/> (built in UiTheme.Build.cs) is installed globally
/// at boot by <see cref="Install"/>, so controls need no per-node theme.
/// </summary>
public static partial class UiTheme
{
    // Panels: Terraria's #3F5297 at ~70% opacity with a dark navy rim.
    public static readonly Color PanelBlue = Color.Color8(63, 82, 151, 178);
    public static readonly Color PanelBorder = Color.Color8(10, 12, 30);
    public static readonly Color PanelHover = Color.Color8(73, 94, 171, 200);
    public static readonly Color ChatPanel = Color.Color8(35, 40, 85, 205);

    // Text.
    public static readonly Color Text = Colors.White;
    public static readonly Color Muted = Color.Color8(190, 196, 220);
    public static readonly Color Gold = Color.Color8(255, 215, 0);
    public static readonly Color Good = Color.Color8(120, 190, 120);
    public static readonly Color Bad = Color.Color8(190, 120, 120);
    public static readonly Color Life = Color.Color8(220, 46, 56);
    public static readonly Color Mana = Color.Color8(56, 115, 242);

    // Coins.
    public static readonly Color Platinum = Color.Color8(220, 220, 198);
    public static readonly Color GoldCoin = Color.Color8(224, 201, 92);
    public static readonly Color Silver = Color.Color8(181, 192, 193);
    public static readonly Color Copper = Color.Color8(246, 138, 96);

    // Chat log messages.
    public static readonly Color BossPurple = Color.Color8(175, 75, 255);
    public static readonly Color NpcArrived = Color.Color8(50, 125, 255);
    public static readonly Color NpcSlain = Color.Color8(255, 25, 25);
    public static readonly Color EventGreen = Color.Color8(50, 255, 130);
    public static readonly Color Death = Color.Color8(225, 25, 25);
    public static readonly Color Housing = Color.Color8(255, 240, 20);

    /// <summary>Slot fill and rim per <see cref="SlotKind"/>, indexed by the enum value.</summary>
    private static readonly (Color Fill, Color Rim)[] SlotColors =
    {
        (Color.Color8(52, 60, 150, 210), Color.Color8(22, 26, 70)), //     Inventory
        (Color.Color8(230, 190, 50, 215), Color.Color8(110, 80, 10)), //   HotbarSelected
        (Color.Color8(40, 95, 150, 210), Color.Color8(16, 40, 66)), //     Chest
        (Color.Color8(45, 110, 125, 210), Color.Color8(16, 46, 52)), //    Shop
        (Color.Color8(85, 75, 170, 210), Color.Color8(34, 28, 80)), //     Equip
        (Color.Color8(60, 50, 120, 210), Color.Color8(24, 18, 56)), //     Vanity
        (Color.Color8(90, 45, 110, 210), Color.Color8(40, 16, 52)), //     Dye
        (Color.Color8(45, 85, 140, 210), Color.Color8(18, 34, 64)), //     Misc
        (Color.Color8(35, 40, 80, 210), Color.Color8(12, 14, 34)), //      Trash
        (Color.Color8(60, 65, 135, 200), Color.Color8(22, 26, 70)), //     Coin
        (Color.Color8(60, 65, 135, 200), Color.Color8(22, 26, 70)), //     Ammo
        (Color.Color8(52, 60, 150, 210), Color.Color8(230, 190, 50)), //   Favorite
        (Color.Color8(52, 60, 150, 210), Color.Color8(22, 26, 70)), //     Crafting
        (Color.Color8(80, 95, 190, 225), Color.Color8(230, 190, 50)), //   CraftingSelected
        (Color.Color8(40, 40, 50, 160), Color.Color8(16, 16, 22)), //      Locked
    };

    private static readonly StyleBoxFlat?[] SlotStyles = new StyleBoxFlat?[SlotColors.Length * 2];
    private static Theme? _theme;

    /// <summary>The full theme for every control type (UiTheme.Build.cs).</summary>
    public static Theme Theme => _theme ??= Build();

    /// <summary>A fresh panel style (callers may change its margins): blue, 2 px navy rim, 10 px rounded corners.</summary>
    public static StyleBoxFlat Panel(float alpha = 1f) => Box(new Color(PanelBlue, PanelBlue.A * alpha), PanelBorder, 2, 10, 12, 10);

    /// <summary>The cached style of a slot of <paramref name="kind"/>; drawing with it allocates nothing.</summary>
    public static StyleBoxFlat SlotStyle(SlotKind kind, bool hovered)
    {
        int index = (int)kind * 2 + (hovered ? 1 : 0);
        if (SlotStyles[index] is { } cached) return cached;
        var (fill, rim) = SlotColors[(int)kind];
        var style = Box(hovered ? fill.Lightened(0.08f) : fill, hovered ? new Color(1f, 1f, 1f, 120f / 255f) : rim, 2, 6, 0, 0);
        if (kind is SlotKind.HotbarSelected or SlotKind.Favorite or SlotKind.CraftingSelected) style.SetBorderWidthAll(3);
        return SlotStyles[index] = style;
    }

    /// <summary>An inventory slot, gold when it is the selected hotbar slot.</summary>
    public static StyleBoxFlat Slot(bool selected, bool hovered) => SlotStyle(selected ? SlotKind.HotbarSelected : SlotKind.Inventory, hovered);

    /// <summary>Item name colour by rarity tier now (Expert and Master tiers animate; see <see cref="RarityColors"/>).</summary>
    public static Color Rarity(int rarity) => Rarity(rarity, Time.GetTicksMsec() / 1000.0);

    /// <summary>Item name colour by rarity tier at <paramref name="seconds"/>.</summary>
    public static Color Rarity(int rarity, double seconds)
    {
        var c = RarityColors.Of(rarity, seconds);
        return Color.Color8(c.R, c.G, c.B);
    }

    /// <summary>True when the rarity colour changes over time, so its text must be recoloured every frame.</summary>
    public static bool IsAnimated(int rarity) => RarityColors.IsAnimated(rarity);

    // Tooltip line colours that no other text shares.
    private static readonly Color Flavor = Color.Color8(190, 200, 255);
    private static readonly Color NoValue = Color.Color8(120, 120, 120);

    /// <summary>
    /// The colour of a tooltip line as Terraria draws it: the name in its rarity colour, prefix bonuses green and
    /// penalties red, a price in the colour of its largest coin, "No value" grey, flavour text lilac, info muted.
    /// </summary>
    public static Color TooltipColor(TooltipKind kind, int rarity) => kind switch
    {
        TooltipKind.Name => Rarity(rarity),
        TooltipKind.Good => Good,
        TooltipKind.Bad => Bad,
        TooltipKind.PriceCopper => Copper,
        TooltipKind.PriceSilver => Silver,
        TooltipKind.PriceGold => GoldCoin,
        TooltipKind.PricePlatinum => Platinum,
        TooltipKind.NoValue => NoValue,
        TooltipKind.Flavor => Flavor,
        TooltipKind.Info => Muted,
        _ => Text,
    };

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int radius, int marginX, int marginY)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = border, AntiAliasing = true };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = style.ContentMarginRight = marginX;
        style.ContentMarginTop = style.ContentMarginBottom = marginY;
        return style;
    }
}
