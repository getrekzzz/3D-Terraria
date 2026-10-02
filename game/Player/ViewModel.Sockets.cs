using Godot;
using Strata.Core.Content;

namespace Strata.Game.Player;

/// <summary>Sockets of the first-person item (P3-10): where its muzzle or tip is on screen, for shot effects.</summary>
public sealed partial class ViewModel
{
    /// <summary>A socket of the held model in world space; false with nothing modelled in hand or the view model
    /// hidden (a model hidden only while its yoyo, flail or whip is out keeps its sockets for the string and the lash).</summary>
    /// <summary>Whether the first-person model is hidden with its weapon out (probe <c>view.held_hidden</c>).</summary>
    public bool HeldHidden => _model is { } m && IsInstanceValid(m) && !m.Visible;

    public bool TrySocket(string name, out Transform3D global)
    {
        global = default;
        if (_item == null || _model is not MeshInstance3D mesh || !IsInstanceValid(mesh) || !mesh.IsInsideTree() || !IsVisibleInTree()) return false;
        if (AssetPaths.ItemModel(_item, _game.Session.Content) is not { } path || _props.Socket(path, name) is not { } local) return false;
        global = mesh.GlobalTransform * local;
        return true;
    }
}
