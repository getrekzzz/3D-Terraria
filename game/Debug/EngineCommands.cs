using Godot;
using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>Commands that need the engine layer (UI screens, camera modes).</summary>
public static class EngineCommands
{
    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "cull", Usage = "cull <on|off>", Help = "Turns cave culling (hiding chunks the camera cannot see into) on or off.",
            Run = (ctx, a) =>
            {
                World.WorldRuntime.CaveCulling = a.Str(0, "on|off") != "off";
                return CommandResult.Success($"Cave culling {(World.WorldRuntime.CaveCulling ? "on" : "off")}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "hud", Usage = "hud <on|off>",
            Help = "Shows or hides every 2D layer (HUD, minimap, screens), e.g. so tour screenshots measure only the 3D scene.",
            Run = (_, a) =>
            {
                bool on = a.Str(0, "on|off") != "off";
                world.Runtime.GetViewport().CanvasCullMask = on ? uint.MaxValue : 0u;
                return CommandResult.Success($"HUD {(on ? "shown" : "hidden")}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "ui", Usage = "ui <inventory|map|talk <resident>|shop|close>", Help = "Opens or closes gameplay screens.",
            Run = (ctx, a) =>
            {
                var screen = world.Inventory ?? throw new CommandException("no inventory screen in this mode");
                var dialog = world.TownDialog ?? throw new CommandException("no town dialog in this mode");
                switch (a.Str(0, "screen"))
                {
                    case "inventory": screen.SetOpen(true); return CommandResult.Success("Inventory open.");
                    case "map":
                        (world.WorldMap ?? throw new CommandException("no map in this mode")).SetOpen(true);
                        return CommandResult.Success("Map open.");
                    case "talk":
                        var id = a.Str(1, "resident");
                        var npc = ctx.World.Town.Find(id) ?? throw new CommandException($"'{id}' does not live in town");
                        if (!ctx.World.Town.BeginTalk(npc, ctx.Player)) return CommandResult.Fail($"{npc.Title} is too far away.");
                        dialog.Open(npc);
                        return CommandResult.Success($"Talking to {npc.Title}.");
                    case "shop":
                        if (!dialog.IsOpen) return CommandResult.Fail("Not talking to anyone.");
                        dialog.OpenShop();
                        return CommandResult.Success("Shop open.");
                    case "close":
                        screen.SetOpen(false);
                        dialog.Close();
                        world.WorldMap?.SetOpen(false);
                        return CommandResult.Success("Closed.");
                    default: throw new CommandException("expected inventory, talk, shop or close");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "camera", Usage = "camera <first|third|orbit <degrees>>",
            Help = "Switches the player camera mode, or swings the third-person camera round the player (aim unchanged).",
            Run = (ctx, a) =>
            {
                var player = world.Player ?? throw new CommandException("no player camera in this mode");
                switch (a.Str(0, "mode"))
                {
                    case "first": player.Rig.FirstPerson = true; break;
                    case "third": player.Rig.FirstPerson = false; break;
                    case "orbit": player.Rig.Orbit = Mathf.DegToRad(a.Float(1, "degrees")); break;
                    default: throw new CommandException("expected first, third or orbit <degrees>");
                }
                return CommandResult.Success();
            },
        });
        r.Register(new CommandDef
        {
            Name = "timescale", Usage = "timescale <factor>",
            Help = "Slows down (below 1) or speeds up the whole game, e.g. to screenshot an animation mid-swing.",
            Run = (_, a) =>
            {
                Engine.TimeScale = Mathf.Clamp(a.Float(0, "factor"), 0.01f, 4f);
                return CommandResult.Success($"Time scale {Engine.TimeScale:0.###}.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "press", Usage = "press <input action> [on|off]",
            Help = "Holds (on, the default) or releases an input action such as move_forward or jump (automated tests).",
            Run = (_, a) =>
            {
                var action = a.Str(0, "action");
                if (!InputMap.HasAction(action)) throw new CommandException($"unknown input action '{action}'");
                if (a.Count > 1 && a.Str(1, "state") == "off") Input.ActionRelease(action);
                else Input.ActionPress(action);
                return CommandResult.Success();
            },
        });
        r.Register(new CommandDef
        {
            Name = "graphics", Usage = "graphics [preset]",
            Help = "Shows the graphics presets, or switches to one (remembered for the next start).",
            Run = (_, a) =>
            {
                var graphics = world.Runtime.Services.Graphics;
                if (a.Count == 0)
                    return CommandResult.Success($"Graphics preset '{graphics.Name}' (available: {string.Join(", ", graphics.Config.Presets.Keys)}).");
                var name = a.Str(0, "preset");
                if (!graphics.Config.Presets.ContainsKey(name)) throw new CommandException($"unknown preset '{name}' (available: {string.Join(", ", graphics.Config.Presets.Keys)})");
                graphics.Select(name, world.Runtime.GetViewport(), remember: true);
                return CommandResult.Success($"Graphics preset '{name}'.");
            },
        });
        r.Register(new CommandDef
        {
            Name = "audio", Usage = "audio [test|files|device <name|Default>]",
            Help = "Shows the audio driver, output device and buses; plays a test chime; checks every sound file; or picks the output device.",
            Run = (_, a) =>
            {
                string info = $"{Audio.AudioDiagnostics.Device()}\nBuses: {Audio.AudioDiagnostics.Buses()}";
                switch (a.Count == 0 ? "" : a.Str(0, "action"))
                {
                    case "":
                        return CommandResult.Success($"{info}\nDevices: {string.Join(" | ", Godot.AudioServer.GetOutputDeviceList())}");
                    case "test":
                        Audio.AudioDiagnostics.PlayTest(world.Runtime);
                        return CommandResult.Success($"Playing a test chime, then a click.\n{info}");
                    case "files":
                        var (loaded, total, failed) = Audio.AudioDiagnostics.ProbeFiles();
                        return failed.Count == 0 ? CommandResult.Success($"All {total} sound files load.")
                            : CommandResult.Fail($"{loaded}/{total} sound files load; failed: {string.Join(", ", failed)}");
                    case "device":
                        var s = Settings.UserSettings.Current;
                        a.Str(1, "device name");
                        s.AudioDevice = string.Join(" ", System.Linq.Enumerable.Skip(a.All, 1)); // device names contain spaces
                        s.Apply();
                        s.Save();
                        return CommandResult.Success($"Output: {Audio.AudioDiagnostics.Device()}");
                    default: throw new CommandException("expected test, files or device");
                }
            },
        });
        r.Register(new CommandDef
        {
            Name = "sky", Usage = "sky clouds <0..1|auto>", Help = "Forces the cloud cover (0 clear … 1 overcast) or returns it to the drifting weather.",
            Run = (_, a) =>
            {
                if (a.Str(0, "what") != "clouds") throw new CommandException("expected: sky clouds <0..1|auto>");
                var env = world.Runtime.Environment;
                if (a.Str(1, "cover") == "auto")
                {
                    env.CloudCoverOverride = null;
                    return CommandResult.Success("Clouds follow the weather.");
                }
                float cover = a.Float(1, "cover");
                if (cover is < 0f or > 1f) throw new CommandException("cover must be within 0..1");
                env.CloudCoverOverride = cover;
                return CommandResult.Success($"Cloud cover {cover:0.##}.");
            },
        });
    }
}
