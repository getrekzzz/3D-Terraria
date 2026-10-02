using Godot;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's pointer as the system mouse cursor on every screen: the generated arrow sprite (assets/ui/cursor.png,
/// white fill, dark rim) tinted by the cursor colour setting (Terraria's pink-red by default) and scaled with the UI
/// in whole pixels. Applied at boot and whenever the settings change.
/// </summary>
public static class UiCursor
{
    /// <summary>The pointer's tip in the sprite, in sprite pixels.</summary>
    private static readonly Vector2 Hotspot = new(1, 1);

    private const int MaxSize = 128;
    private static string? _applied;
    private static ImageTexture? _texture;

    public static void Apply(UserSettings s)
    {
        if (DisplayServer.GetName() == "headless") return;
        int scale = Mathf.Clamp(Mathf.RoundToInt(DisplaySettings.EffectiveUiScale), 1, 4);
        string key = s.CursorColor + "@" + scale;
        if (key == _applied) return;
        var path = UiSprites.PathFor("cursor");
        if (!ResourceLoader.Exists(path)) return; // UiSprites reports missing sprites
        var image = GD.Load<Texture2D>(path).GetImage();
        if (image == null) return;
        image.Decompress();
        image.Convert(Image.Format.Rgba8);
        var tint = new Color(Color.HtmlIsValid(s.CursorColor) ? Color.FromHtml(s.CursorColor) : Color.FromHtml(UserSettings.DefaultCursorColor), 1f);
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                image.SetPixel(x, y, image.GetPixel(x, y) * tint);
        if (scale > 1 && image.GetWidth() * scale <= MaxSize)
            image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
        else scale = 1;
        var texture = ImageTexture.CreateFromImage(image);
        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, Hotspot * scale);
        Input.SetCustomMouseCursor(texture, Input.CursorShape.PointingHand, Hotspot * scale);
        _texture?.Dispose();
        _texture = texture;
        _applied = key;
    }

    /// <summary>Gives the system pointer back and frees the texture while the renderer still exists (on quit).</summary>
    public static void Reset()
    {
        if (_texture == null) return;
        Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
        Input.SetCustomMouseCursor(null, Input.CursorShape.PointingHand);
        _texture.Dispose();
        _texture = null;
        _applied = null;
    }
}
