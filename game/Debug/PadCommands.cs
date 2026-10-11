using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Commands;
using Strata.Core.Presentation;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// The <c>pad</c> command: a simulated controller for tests. Its button presses, stick tilts and trigger pulls are
/// real controller input events, parsed by the engine as a connected controller's would be, so the input actions, the
/// device switch (<see cref="InputDevice"/>), the screen cursor and every handler see exactly what a controller sends.
/// </summary>
public static class PadCommands
{
    /// <summary>Seconds a tapped button stays down (long enough for a rendered frame and a physics tick to see it).</summary>
    private const double TapSeconds = 0.12;

    private static readonly HashSet<int> Held = new();

    public static void Register(CommandRegistry r, LaunchedWorld world)
    {
        r.Register(new CommandDef
        {
            Name = "pad",
            Usage = "pad <button> [down|up] | pad stick <left|right> <x> <y> | pad trigger <lt|rt> <0..1> | pad release | pad mouse | pad assist <on|off>",
            Help = $"Simulated controller (Xbox names): taps a button ({string.Join(", ", InputBinding.PadButtons)}) or holds (down) or "
                + "releases (up) it; tilts a stick (x right, y down, -1..1); pulls a trigger; release lets go of everything; mouse "
                + "switches back to keyboard and mouse as a key press would; assist turns aim assist on or off for this session.",
            Run = (_, a) =>
            {
                var viewport = world.Runtime.GetViewport();
                string what = a.Str(0, "button");
                switch (what)
                {
                    case "stick":
                        string side = a.Str(1, "left|right");
                        if (side is not ("left" or "right")) throw new CommandException("the stick is left or right");
                        float x = Math.Clamp(a.Float(2, "x"), -1f, 1f), y = Math.Clamp(a.Float(3, "y"), -1f, 1f);
                        int axis = side == "left" ? 0 : 2;
                        Send(viewport, Motion(axis, x));
                        Send(viewport, Motion(axis + 1, y));
                        return CommandResult.Success($"{side} stick at ({x:0.##}, {y:0.##}).");
                    case "trigger":
                        string trigger = a.Str(1, "lt|rt");
                        if (trigger is not ("lt" or "rt")) throw new CommandException("the trigger is lt or rt");
                        float pull = Math.Clamp(a.Float(2, "pull"), 0f, 1f);
                        Send(viewport, Motion(trigger == "lt" ? 4 : 5, pull));
                        return CommandResult.Success($"{trigger} at {pull:0.##}.");
                    case "release":
                        foreach (int held in new List<int>(Held)) Send(viewport, Button(held, false));
                        for (int i = 0; i < InputBinding.PadAxes.Length; i++) Send(viewport, Motion(i, 0));
                        return CommandResult.Success("Controller at rest.");
                    case "mouse":
                        InputDevice.Set(false);
                        return CommandResult.Success("Keyboard and mouse in use.");
                    case "assist":
                        Settings.UserSettings.Current.AimAssist = a.Str(1, "on|off") != "off";
                        return CommandResult.Success($"Aim assist {(Settings.UserSettings.Current.AimAssist ? "on" : "off")}.");
                }
                int button = Array.IndexOf(InputBinding.PadButtons, what);
                if (button < 0) throw new CommandException($"unknown button '{what}' (expected {string.Join(", ", InputBinding.PadButtons)}, stick, trigger, release, mouse or assist)");
                switch (a.OptStr(1))
                {
                    case "down":
                        Send(viewport, Button(button, true));
                        return CommandResult.Success($"Holding {what}.");
                    case "up":
                        Send(viewport, Button(button, false));
                        return CommandResult.Success($"Released {what}.");
                    case null:
                        Send(viewport, Button(button, true));
                        world.Runtime.GetTree().CreateTimer(TapSeconds, processAlways: true).Timeout += () => Send(viewport, Button(button, false));
                        return CommandResult.Success($"Tapped {what}.");
                    default:
                        throw new CommandException("expected down or up");
                }
            },
        });
    }

    private static InputEventJoypadButton Button(int index, bool pressed)
    {
        if (pressed) Held.Add(index);
        else Held.Remove(index);
        return new InputEventJoypadButton { Device = 0, ButtonIndex = (JoyButton)index, Pressed = pressed, Pressure = pressed ? 1f : 0f };
    }

    private static InputEventJoypadMotion Motion(int axis, float value) => new() { Device = 0, Axis = (JoyAxis)axis, AxisValue = value };

    /// <summary>
    /// Into the engine's input as a controller's event: the action states follow, and the event is dispatched to the
    /// scene (handlers, the GUI).
    /// </summary>
    private static void Send(Viewport viewport, InputEvent e)
    {
        InputDevice.Observe(e);
        Input.ParseInputEvent(e);
    }
}
