using Strata.Core.Commands;
using Strata.Core.Presentation;
using Strata.Game.Bootstrap;

namespace Strata.Game.UI.PhotoMode;

/// <summary>The <c>photo</c> command (X-12): enters and leaves photo mode, takes a picture and sets the lens.</summary>
public static class PhotoCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "photo", Usage = "photo <on|off|shot|dof <focus m> <blur 0..1>|exposure <stops>|filter <none|warm|cool|mono|sepia>>",
            Help = "Photo mode (key P): pauses the world, flies a free camera, hides the HUD; shot saves user://screenshots/<time>.png.",
            Run = (_, a) =>
            {
                var photo = world.PhotoMode ?? throw new CommandException("no photo mode in this mode");
                switch (a.Str(0, "action"))
                {
                    case "on": photo.SetActive(true); return CommandResult.Success("Photo mode on.");
                    case "off": photo.SetActive(false); return CommandResult.Success("Photo mode off.");
                    case "shot":
                        if (!photo.Active) throw new CommandException("not in photo mode");
                        photo.TakeShot();
                        return CommandResult.Success("Picture requested.");
                    case "dof":
                        photo.SetLens(focus: a.Float(1, "focus"), blur: a.Float(2, "blur"));
                        return CommandResult.Success($"Focus {photo.Focus:0.#} m, blur {photo.Blur:0.##}.");
                    case "exposure":
                        photo.SetLens(stops: a.Float(1, "stops"));
                        return CommandResult.Success($"Exposure {photo.Stops:+0.#;-0.#;0} EV.");
                    case "filter":
                        int index = PhotoLens.FilterIndex(a.Str(1, "filter"));
                        if (index < 0) throw new CommandException($"expected {string.Join(", ", PhotoLens.Filters)}");
                        photo.SetLens(filter: index);
                        return CommandResult.Success($"Filter {PhotoLens.Filters[index]}.");
                    default: throw new CommandException("expected on, off, shot, dof, exposure or filter");
                }
            },
        });
    }
}

/// <summary>Photo mode probes: active, pictures requested/saved, HUD hidden, lens state, and whether the world simulates.</summary>
[ProbeSource]
public static class PhotoProbes
{
    public static void Register(CommandContext ctx, LaunchedWorld world)
    {
        var photo = world.PhotoMode;
        ctx.AddProbe("world.simulating", () => world.Runtime.Simulating ? 1 : 0);
        ctx.AddProbe("photo.active", () => photo?.Active == true ? 1 : 0);
        ctx.AddProbe("photo.shots", () => photo?.Shots ?? 0);
        ctx.AddProbe("photo.saved", () => photo?.Saved ?? 0);
        ctx.AddProbe("photo.hud_hidden", () => photo?.HudHidden == true ? 1 : 0);
        ctx.AddProbe("photo.dof", () => photo?.DofEnabled == true ? 1 : 0);
        ctx.AddProbe("photo.filter", () => photo?.Filter ?? 0);
    }
}
