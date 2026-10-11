using Godot;

namespace Strata.Game.UI;

/// <summary>
/// A controller button drawn as a prompt (the crosshair's interact hint): a face button as a filled disc in its Xbox
/// colour with the letter on it, anything else (bumpers, triggers, the D-pad, sticks) as a dark rounded plate with its
/// name. Drawn with the canvas, no textures.
/// </summary>
public static class PadGlyph
{
    private static readonly Color Plate = new(0.12f, 0.13f, 0.17f, 0.92f);
    private static readonly Color Rim = new(0.85f, 0.88f, 0.95f, 0.9f);

    /// <summary>The colour of a face button, or null for other buttons.</summary>
    private static Color? FaceColor(string label) => label switch
    {
        "A" => Color.Color8(96, 186, 70),
        "B" => Color.Color8(220, 70, 60),
        "X" => Color.Color8(60, 130, 230),
        "Y" => Color.Color8(240, 190, 40),
        _ => null,
    };

    /// <summary>Draws <paramref name="label"/>'s glyph with its left edge at <paramref name="left"/>, centred on its y; returns its width.</summary>
    public static float Draw(CanvasItem canvas, Vector2 left, string label, int size)
    {
        var font = UiFonts.Bold;
        float height = size + 6;
        if (FaceColor(label) is { } face)
        {
            float r = height / 2;
            var centre = left + new Vector2(r, 0);
            canvas.DrawCircle(centre, r + 1, Rim);
            canvas.DrawCircle(centre, r, face);
            var w = font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
            canvas.DrawString(font, centre + new Vector2(-w / 2, size * 0.36f), label, HorizontalAlignment.Left, -1, size, Colors.White);
            return height;
        }
        float textWidth = font.GetStringSize(label, HorizontalAlignment.Left, -1, size - 2).X;
        var rect = new Rect2(left - new Vector2(0, height / 2), new Vector2(textWidth + 10, height));
        canvas.DrawRect(rect.Grow(1), Rim, false, 1);
        canvas.DrawRect(rect, Plate);
        canvas.DrawString(font, new Vector2(rect.Position.X + 5, left.Y + (size - 2) * 0.36f), label, HorizontalAlignment.Left, -1, size - 2, Colors.White);
        return rect.Size.X;
    }
}
