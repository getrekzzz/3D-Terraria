using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Voxels;

namespace Strata.Game.UI;

/// <summary>Picks a world cell under the free mouse cursor (the housing menu's query and assign clicks).</summary>
public static class WorldPicker
{
    /// <summary>Furthest pick (metres).</summary>
    public const float Range = 40f;

    /// <summary>The open cell in front of what the mouse points at (inside a room for a wall or floor), or null.</summary>
    public static Int3? AirCellUnderMouse(Viewport viewport, VoxelWorld world)
    {
        var camera = viewport.GetCamera3D();
        if (camera == null) return null;
        var mouse = viewport.GetMousePosition();
        var o = camera.ProjectRayOrigin(mouse);
        var d = camera.ProjectRayNormal(mouse);
        var hit = Targeting.Cast(world, new System.Numerics.Vector3(o.X, o.Y, o.Z), new System.Numerics.Vector3(d.X, d.Y, d.Z), Range, TargetFilter.None);
        if (hit == null) return null;
        return hit.Object != null ? hit.Cell : hit.Adjacent;
    }
}
