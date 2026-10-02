using Godot;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.Menu;

/// <summary>The Accessibility tab (X-12): colour-blind modes and the filter's strength, and subtitles for sounds.</summary>
public sealed partial class SettingsScreen
{
    private static readonly string[] ColorBlindNames =
    {
        "Off", "Protanopia (red-weak)", "Deuteranopia (green-weak)", "Tritanopia (blue-weak)",
    };

    private void Accessibility(VBoxContainer box)
    {
        box.AddChild(Header("Colour vision"));
        box.AddChild(MenuKit.Row("Colour-blind mode", MenuKit.Choice(ColorBlindNames, (int)ColorVision.Parse(S.ColorBlindMode),
                i => { S.ColorBlindMode = ColorVision.Name((ColorBlindMode)i); Changed(); }),
            "Recolours the world so reds, greens and blues that look alike stand apart, and swaps the interface's item rarity, " +
            "chat and caption colours for ones tuned to stay distinct."));
        box.AddChild(MenuKit.Row("Filter strength", MenuKit.Slider(0, 1, 0.05, S.ColorBlindStrength,
                v => { S.ColorBlindStrength = (float)v; Changed(); }, v => $"{v * 100:0}%"),
            "How strongly the world is recoloured. The interface colours swap fully whenever a mode is on."));
        box.AddChild(Header("Sound"));
        box.AddChild(MenuKit.Row("Subtitles", MenuKit.Toggle(S.Subtitles, v => { S.Subtitles = v; Changed(); }),
            "Captions above the chat log for important sounds: bosses, residents arriving, rain and thunder, and nearby " +
            "creatures with an arrow pointing to where they are."));
    }
}
