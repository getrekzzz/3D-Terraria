using Godot;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Game.Player;

public sealed partial class PlayerController
{
    /// <summary>Deepest sink (m) into a freshly placed cube that is undone; anything deeper is left to the game.</summary>
    private const float MaxUnbury = 0.3f;

    /// <summary>
    /// Voxel edits apply at once but a chunk's collider is only rebuilt a little later (the old body stays until its
    /// replacement exists). A body standing over a hole that has just been filled can therefore drop into the stale
    /// hole and end up inside the new block, which also puts its feet cell one too low for anything that works
    /// relative to it. When the feet are a little way inside a full player-placed cube with room above, the body is
    /// lifted onto the cube's top. Natural smoothed ground and partial shapes (where bodies rest slightly inside the
    /// cell on purpose) are left alone.
    /// </summary>
    private void Unbury()
    {
        var world = _runtime.Session.World;
        var p = GlobalPosition;
        float cs = Units.CellSize;
        int x = Mathf.FloorToInt(p.X / cs), y = Mathf.FloorToInt((p.Y + 0.02f) / cs), z = Mathf.FloorToInt(p.Z / cs);
        if (!world.IsSolid(x, y, z) || world.GetShape(x, y, z) != 0 || (world.GetFlags(x, y, z) & CellFlags.PlayerPlaced) == 0) return;
        float top = (y + 1) * cs;
        if (top - p.Y > MaxUnbury) return;
        for (int dy = 1; dy <= 4; dy++)
            if (world.IsSolid(x, y + dy, z)) return; // no room to stand on it
        GlobalPosition = new Vector3(p.X, top + 0.001f, p.Z);
        if (Velocity.Y < 0) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
    }
}
