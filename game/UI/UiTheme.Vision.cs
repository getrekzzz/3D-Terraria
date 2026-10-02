using Godot;
using Strata.Core.Content.Loading;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// The colour-blind interface palette (X-12): in a colour-blind mode the key UI colours (rarity names, chat lines,
/// captions, tooltip bonuses and penalties) are swapped for the tuned ones of data/config/color_vision.json
/// (<see cref="ColorVisionPalette"/>); every other colour, and everything with the mode off, passes through. The world
/// view is recoloured by <see cref="Rendering.ColorBlindFilter"/> instead.
/// </summary>
public static partial class UiTheme
{
    private static ColorVisionPalette? _palette;

    /// <summary>The colour-blind mode the interface draws for.</summary>
    public static ColorBlindMode VisionMode { get; private set; }

    /// <summary>Changes with the mode, so widgets holding coloured text (the chat log) recolour it.</summary>
    public static int VisionVersion { get; private set; }

    /// <summary>Loads the palettes and follows the setting (once, at boot).</summary>
    public static void InstallVision(IDataSource data)
    {
        _palette = ColorVisionPalette.Load(data);
        ApplyVision(UserSettings.Current);
        UserSettings.Current.Changed += ApplyVision;
    }

    private static void ApplyVision(UserSettings s)
    {
        var mode = ColorVision.Parse(s.ColorBlindMode);
        if (mode == VisionMode) return;
        VisionMode = mode;
        VisionVersion++;
    }

    /// <summary><paramref name="c"/> as the interface shows it in the current colour-blind mode (alpha kept).</summary>
    public static Color Vision(Color c)
    {
        if (VisionMode == ColorBlindMode.Off || _palette == null) return c;
        var from = new Rgb((byte)c.R8, (byte)c.G8, (byte)c.B8);
        var to = _palette.Apply(from, VisionMode);
        return to == from ? c : Color.Color8(to.R, to.G, to.B, (byte)c.A8);
    }
}
