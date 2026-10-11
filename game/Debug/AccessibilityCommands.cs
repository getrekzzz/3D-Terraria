using Godot;
using Strata.Core.Commands;
using Strata.Core.Presentation;
using Strata.Game.Audio;
using Strata.Game.Bootstrap;
using Strata.Game.Rendering;
using Strata.Game.Settings;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// The <c>access</c> command (X-12): turns the subtitles on or off, picks a colour-blind mode and strength (in memory,
/// like the settings menu before it is closed), and plays a sound around the camera so its caption can be checked.
/// </summary>
public static class AccessibilityCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "access", Usage = "access <subtitles <on|off>|colorblind <off|protanopia|deuteranopia|tritanopia> [strength 0..1]|hear <sound> [ahead|behind|left|right]>",
            Help = "Accessibility settings (Settings > Accessibility): subtitles, the colour-blind mode; hear plays a sound 6 m from the camera (or a loop's caption without a position).",
            Run = (_, a) =>
            {
                var s = UserSettings.Current;
                switch (a.Str(0, "what"))
                {
                    case "subtitles":
                        s.Subtitles = OnOff(a.Str(1, "on|off"));
                        s.Apply();
                        return CommandResult.Success($"Subtitles {(s.Subtitles ? "on" : "off")}.");
                    case "colorblind":
                        string name = a.Str(1, "mode");
                        var mode = ColorVision.Parse(name);
                        if (mode == ColorBlindMode.Off && name != "off") throw new CommandException($"expected {string.Join(", ", ColorVision.ModeNames)}");
                        s.ColorBlindMode = ColorVision.Name(mode);
                        if (a.OptStr(2) != null) s.ColorBlindStrength = Mathf.Clamp(a.Float(2, "strength"), 0f, 1f);
                        s.Apply();
                        return CommandResult.Success($"Colour-blind mode {s.ColorBlindMode} at {s.ColorBlindStrength * 100:0}%.");
                    case "hear":
                        return Hear(world, a.Str(1, "sound"), a.OptStr(2));
                    default: throw new CommandException("expected subtitles, colorblind or hear");
                }
            },
        });
    }

    private static CommandResult Hear(LaunchedWorld world, string id, string? where)
    {
        if (!Sfx.Has(id))
        {
            Sfx.Report(id); // an ambience loop or thunder: heard without a position
            return CommandResult.Success($"Reported {id}.");
        }
        if (where == null)
        {
            Sfx.Play(id);
            return CommandResult.Success($"Played {id}.");
        }
        var camera = world.Runtime.GetViewport().GetCamera3D() ?? throw new CommandException("no camera");
        var basis = camera.GlobalBasis;
        var offset = where switch
        {
            "ahead" => -basis.Z,
            "behind" => basis.Z,
            "left" => -basis.X,
            "right" => basis.X,
            _ => throw new CommandException("expected ahead, behind, left or right"),
        };
        offset.Y = 0;
        Sfx.PlayAt(id, camera.GlobalPosition + offset.Normalized() * 6f);
        return CommandResult.Success($"Played {id} {where}.");
    }

    private static bool OnOff(string v) => v switch
    {
        "on" => true,
        "off" => false,
        _ => throw new CommandException("expected on or off"),
    };
}

/// <summary>
/// Accessibility probes (X-12): <c>ui.captions</c> (subtitle lines on screen), <c>ui.caption_arrows</c> (of them, with a
/// direction arrow), <c>ui.captions_weather</c> (of them, weather lines), <c>ui.saving</c> (the autosave spinner shows), <c>ui.save_icons</c> (times it appeared),
/// <c>ui.colorblind</c> (the filter's mode: 0 off, 1 protanopia, 2 deuteranopia, 3 tritanopia), <c>ui.colorblind_pass</c>
/// (the filter pass draws) and <c>ui.palette_swapped</c> (the interface shows the Green rarity in a tuned colour).
/// </summary>
[ProbeSource]
public static class AccessibilityProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        ctx.AddProbe("ui.captions", () => world.Hud?.Captions.LinesShown ?? 0);
        ctx.AddProbe("ui.caption_arrows", () => world.Hud?.Captions.ArrowsShown ?? 0);
        ctx.AddProbe("ui.captions_weather", () => world.Hud?.Captions.LinesOfKind("weather") ?? 0);
        ctx.AddProbe("ui.saving", () => world.Hud?.SaveSpinner.Showing == true ? 1 : 0);
        ctx.AddProbe("ui.save_icons", () => world.Hud?.SaveSpinner.Shown ?? 0);
        ctx.AddProbe("ui.colorblind", () => (int)(ColorBlindFilter.Instance?.Mode ?? ColorBlindMode.Off));
        ctx.AddProbe("ui.colorblind_pass", () => ColorBlindFilter.Instance?.Active == true ? 1 : 0);
        ctx.AddProbe("ui.palette_swapped", () =>
        {
            var plain = RarityColors.Of(2);
            return UiTheme.Rarity(2, 0) != Color.Color8(plain.R, plain.G, plain.B) ? 1 : 0;
        });
    }
}
