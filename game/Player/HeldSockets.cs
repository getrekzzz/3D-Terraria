using Godot;

namespace Strata.Game.Player;

/// <summary>
/// Where the local player's held item has its business end in the view on screen (P3-10): the first-person item's
/// socket while in first person, else the third-person avatar's. Shot effects and projectiles leaving the barrel use it.
/// </summary>
public static class HeldSockets
{
    private static ViewModel? _view;
    private static PlayerAvatar? _avatar;

    /// <summary>The business-end sockets tried in order: a gun's muzzle, a staff's or wand's tip.</summary>
    private static readonly string[] Ends = { "muzzle", "tip" };

    public static void Register(ViewModel view, PlayerAvatar avatar)
    {
        _view = view;
        _avatar = avatar;
    }

    /// <summary>The held item's business end in world space, if the shown model has one.</summary>
    public static bool TryBusinessEnd(out Vector3 position)
    {
        foreach (var name in Ends)
            if (TryNamed(name, out position)) return true;
        position = default;
        return false;
    }

    /// <summary>A named socket of the held model (grip, string, chain…) in the active view, in world space.</summary>
    public static bool TryNamed(string name, out Vector3 position)
    {
        if (_view != null && GodotObject.IsInstanceValid(_view) && _view.TrySocket(name, out var fp))
        {
            position = fp.Origin;
            return true;
        }
        if (_avatar != null && GodotObject.IsInstanceValid(_avatar) && _avatar.IsVisibleInTree() && _avatar.TrySocket(name, out var tp))
        {
            position = tp.Origin;
            return true;
        }
        position = default;
        return false;
    }
}
