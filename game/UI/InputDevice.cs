using System;
using Godot;

namespace Strata.Game.UI;

/// <summary>
/// Whether the player is on a controller or on the keyboard and mouse: whichever was used last. A controller button or
/// a firm stick or trigger push switches to the controller; a key, a mouse button or a real mouse movement switches
/// back. Stick drift and the motion that follows the controller cursor moving the system pointer
/// (<see cref="Warped"/>) never count. Button prompts, the screen cursor (<see cref="PadCursor"/>), menu focus and aim
/// assist follow it.
/// </summary>
public static class InputDevice
{
    /// <summary>How far (0–1) a stick or trigger must be pushed to count as using the controller.</summary>
    private const float StickThreshold = 0.4f;

    /// <summary>Mouse motion smaller than this (pixels) is a nudge, not a switch back to the mouse.</summary>
    private const float MouseThreshold = 3f;

    /// <summary>Milliseconds after the pointer was moved for the controller during which mouse motion near it is ignored.</summary>
    private const ulong WarpWindowMs = 150;

    private static Vector2 _warp;
    private static ulong _warpMs;

    /// <summary>The controller was used last.</summary>
    public static bool UsingPad { get; private set; }

    /// <summary>Counts the switches, so prompts can tell they are stale without an event.</summary>
    public static int Version { get; private set; }

    /// <summary>The device in use changed (true: now the controller).</summary>
    public static event Action<bool>? Changed;

    /// <summary>Notes the device of an input event (every event passes here first, from <see cref="PadInput"/>).</summary>
    public static void Observe(InputEvent e)
    {
        bool? pad = e switch
        {
            InputEventJoypadButton { Pressed: true } => true,
            InputEventJoypadMotion m when Mathf.Abs(m.AxisValue) >= StickThreshold => true,
            InputEventKey { Pressed: true } => false,
            InputEventMouseButton { Pressed: true } => false,
            InputEventMouseMotion m when RealMotion(m) => false,
            _ => null,
        };
        if (pad is { } p && p != UsingPad) Set(p);
    }

    /// <summary>Switches the device (tests, and <see cref="Observe"/>).</summary>
    public static void Set(bool pad)
    {
        if (pad == UsingPad) return;
        UsingPad = pad;
        Version++;
        Changed?.Invoke(pad);
    }

    /// <summary>The controller cursor moved the system pointer to <paramref name="at"/>: the motion that follows is not the player's.</summary>
    public static void Warped(Vector2 at)
    {
        _warp = at;
        _warpMs = Time.GetTicksMsec();
    }

    private static bool RealMotion(InputEventMouseMotion m) =>
        m.Relative.LengthSquared() >= MouseThreshold * MouseThreshold
        && (Time.GetTicksMsec() - _warpMs > WarpWindowMs || m.Position.DistanceTo(_warp) > 4f);
}
