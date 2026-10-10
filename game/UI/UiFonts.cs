using Godot;

namespace Strata.Game.UI;

/// <summary>
/// The UI typeface: Comic Neue Bold (SIL OFL, assets/fonts), the closest open face to Terraria's chunky lettering, with
/// Andika Bold as the glyph fallback (wired in the font's import settings and again here). Text is drawn white with a
/// black outline everywhere; the size and outline constants keep screens consistent.
/// </summary>
public static class UiFonts
{
    public const string BoldPath = "res://assets/fonts/ComicNeue-Bold.otf";
    public const string FallbackPath = "res://assets/fonts/Andika-Bold.ttf";

    public const int Small = 13;
    public const int Body = 16;
    public const int Stack = 14;
    public const int Header = 20;

    /// <summary>Hover names beside the pointer or crosshair (a creature under the crosshair, a map marker), as Terraria's.</summary>
    public const int Hover = 18;
    public const int Banner = 26;
    public const int Death = 48;

    /// <summary>Black outline width for body text.</summary>
    public const int Outline = 4;

    /// <summary>Black outline width for large text (headers, banners, the death message).</summary>
    public const int OutlineLarge = 8;

    public static readonly Color OutlineColor = new(0f, 0f, 0f, 1f);

    private static FontFile? _bold;

    /// <summary>The UI font, loaded once (the resource cache shares it with the theme).</summary>
    public static FontFile Bold => _bold ??= Load();

    private static FontFile Load()
    {
        var font = GD.Load<FontFile>(BoldPath);
        if (font.Fallbacks.Count == 0) font.Fallbacks = new Godot.Collections.Array<Font> { GD.Load<FontFile>(FallbackPath) };
        return font;
    }

    /// <summary>
    /// Rasterises printable ASCII for every (size, outline) pair the UI uses, so opening a screen never rasterises glyphs
    /// mid-game. Returns the number of pairs rendered.
    /// </summary>
    public static int Warmup()
    {
        // Outlined text draws each glyph twice: the outline at (size, outline), then the fill at (size, 0). The Death size
        // is left out: its glyphs cost more than all the others together, and it only shows once play has stopped.
        var font = Bold;
        int pairs = 0;
        foreach (var (size, outline) in new[] { (Small, Outline), (Stack, Outline), (Body, Outline), (Hover, Outline), (Header, Outline), (Banner, OutlineLarge) })
        {
            font.RenderRange(0, new Vector2I(size, 0), 32, 126);
            font.RenderRange(0, new Vector2I(size, outline), 32, 126);
            pairs += 2;
        }
        return pairs;
    }
}
