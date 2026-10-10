using Godot;
using Strata.Core.Commands;
using Strata.Game.Bootstrap;
using Strata.Game.UI;

namespace Strata.Game.Debug;

/// <summary>
/// Synthetic mouse input routed through the GUI as a real pointer's is (the <c>ui click</c> and <c>ui drag</c>
/// commands): presses go to whatever control is under the point, so a test proves the empty screen gets them.
/// </summary>
public static class PointerInput
{
    private static Vector2 _held;

    /// <summary><c>ui click left|right [down|up]</c>: a press on empty screen, released at once unless <c>down</c>.</summary>
    public static CommandResult Click(LaunchedWorld world, string button, string? phase)
    {
        var index = button switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            _ => throw new CommandException("expected left or right"),
        };
        var catcher = Catcher(world);
        if (phase == "up")
        {
            Button(catcher.GetViewport(), _held, index, false);
            return CommandResult.Success($"Released the {button} button.");
        }
        if (phase is not (null or "down")) throw new CommandException("expected down or up");
        _held = FindEmptyScreen(catcher) ?? throw new CommandException("no empty screen to click (open the inventory or a chat first)");
        Button(catcher.GetViewport(), _held, index, true);
        if (phase == null) Button(catcher.GetViewport(), _held, index, false);
        return CommandResult.Success($"Clicked {button} on empty screen at {_held}.");
    }

    /// <summary><c>ui drag dx dy</c>: a right-button drag on empty screen by (dx, dy) pixels.</summary>
    public static CommandResult Drag(LaunchedWorld world, float dx, float dy)
    {
        var catcher = Catcher(world);
        var viewport = catcher.GetViewport();
        var at = FindEmptyScreen(catcher) ?? throw new CommandException("no empty screen to drag on (open the inventory or a chat first)");
        var by = new Vector2(dx, dy);
        Button(viewport, at, MouseButton.Right, true);
        Move(viewport, at + by, by, MouseButtonMask.Right);
        Button(viewport, at + by, MouseButton.Right, false);
        return CommandResult.Success($"Dragged by {by}.");
    }

    public static WorldClickCatcher Catcher(LaunchedWorld world) =>
        world.Runtime.GetNodeOrNull<WorldClickCatcher>(WorldClickCatcher.NodePath) ?? throw new CommandException("no gameplay screens in this mode");

    /// <summary>
    /// A point on empty screen, where a press reaches <paramref name="catcher"/> rather than a panel: found by probing a
    /// grid, bottom rows first, with the side mouse button, which no slot, button or list reacts to. Null when none is.
    /// </summary>
    public static Vector2? FindEmptyScreen(WorldClickCatcher catcher)
    {
        var viewport = catcher.GetViewport();
        var size = viewport.GetVisibleRect().Size;
        for (int row = 7; row >= 1; row--)
            for (int col = 1; col < 12; col++)
            {
                var at = new Vector2(size.X * col / 12f, size.Y * row / 8f);
                int before = catcher.Presses;
                Button(viewport, at, MouseButton.Xbutton1, true);
                Button(viewport, at, MouseButton.Xbutton1, false);
                if (catcher.Presses != before) return at;
            }
        return null;
    }

    public static void Button(Viewport viewport, Vector2 at, MouseButton button, bool pressed)
    {
        var mask = pressed && button is MouseButton.Left or MouseButton.Right or MouseButton.Middle
            ? (MouseButtonMask)(1 << ((int)button - 1))
            : 0;
        viewport.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed, ButtonMask = mask }, true);
    }

    public static void Move(Viewport viewport, Vector2 at, Vector2 relative, MouseButtonMask held) =>
        viewport.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = relative, ButtonMask = held }, true);
}
