using Godot;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// An amount written as Terraria writes a price: a white label, then each coin in its own colour ('Cost: 1 gold
/// 20 silver' in gold, then silver), in the outlined UI font, vertically centred and clipped to the control. Like
/// <see cref="OutlinedText"/> it has no minimum size; it redraws only when the label or the amount changes.
/// </summary>
public sealed partial class CoinText : Control
{
    private static readonly (string Name, Color Colour)[] Tiers =
    {
        ("platinum", UiTheme.Platinum), ("gold", UiTheme.GoldCoin), ("silver", UiTheme.Silver), ("copper", UiTheme.Copper),
    };

    private string _label = "";
    private long _copper;

    public CoinText()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    /// <summary>The white text before the coins ('Cost:'); a space follows it.</summary>
    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            QueueRedraw();
        }
    }

    /// <summary>The amount in copper; nothing but the label is drawn for zero.</summary>
    public long Copper
    {
        get => _copper;
        set
        {
            if (_copper == value) return;
            _copper = value;
            QueueRedraw();
        }
    }

    public int FontSize { get; set; } = UiFonts.Body;

    public override void _Draw()
    {
        var font = UiFonts.Bold;
        float ascent = font.GetAscent(FontSize), descent = font.GetDescent(FontSize);
        var at = new Vector2(0, Mathf.Round((Size.Y + ascent - descent) / 2));
        if (_label.Length > 0) at.X = Part(font, at, _label + " ", UiTheme.Text);
        var parts = Coins.Parts(_copper);
        long[] counts = { parts.Platinum, parts.Gold, parts.Silver, parts.Copper };
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0) at.X = Part(font, at, $"{counts[i]} {Tiers[i].Name} ", Tiers[i].Colour);
    }

    /// <summary>Draws one outlined run at <paramref name="at"/> and returns where the next one starts.</summary>
    private float Part(Font font, Vector2 at, string text, Color colour)
    {
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, FontSize, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, FontSize, colour);
        return at.X + font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
    }
}
