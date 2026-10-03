using Godot;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Game.Player;

public sealed partial class PlayerController
{
    /// <summary>Deepest burial (cells) that is undone; anything deeper is left to the game.</summary>
    private const int MaxUnburyCells = 4;

    /// <summary>
    /// Voxel edits apply at once but a chunk's collider is only rebuilt a little later (the old body stays until its
    /// replacement exists). A body over ground that has just been filled (a debug or smoke `fill` around a falling
    /// player) can therefore drop through the stale surface and end up inside the new blocks, where it stays or is
    /// pushed out slowly, which puts its feet cell, and everything that works relative to it, a cell or more too low.
    /// When the feet are inside full player-placed cubes with room to stand above them within
    /// <see cref="MaxUnburyCells"/>, the body is lifted onto the top at once. Natural smoothed ground and partial
    /// shapes (where bodies rest slightly inside the cell on purpose) are left alone.
    /// </summary>
    private void Unbury()
    {
        var world = _runtime.Session.World;
        var p = GlobalPosition;
        float cs = Units.CellSize;
        int x = Mathf.FloorToInt(p.X / cs), y = Mathf.FloorToInt((p.Y + 0.02f) / cs), z = Mathf.FloorToInt(p.Z / cs);
        int k = 0;
        while (k < MaxUnburyCells && world.IsSolid(x, y + k, z))
        {
            if (world.GetShape(x, y + k, z) != 0 || (world.GetFlags(x, y + k, z) & CellFlags.PlayerPlaced) == 0) return;
            k++;
        }
        if (k == 0 || k == MaxUnburyCells) return;
        for (int dy = 0; dy < 4; dy++)
            if (world.IsSolid(x, y + k + dy, z)) return; // no room to stand on top
        GlobalPosition = new Vector3(p.X, (y + k) * cs + 0.001f, p.Z);
        if (Velocity.Y < 0) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
    }
}
