using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Voxels;

namespace Strata.Game.Rendering;

/// <summary>
/// Samples the Core voxel light (computed by mesh jobs) at a point so entities, props and drops are lit exactly
/// like the terrain around them. Returns rgb = block light, a = sky exposure, each 0–1.
/// </summary>
public static class LightProbe
{
    public static Color Sample(VoxelWorld world, Vector3 meters)
    {
        var cell = new Int3(Mathf.FloorToInt(meters.X / Units.CellSize), Mathf.FloorToInt(meters.Y / Units.CellSize), Mathf.FloorToInt(meters.Z / Units.CellSize));
        ushort best = Read(world, cell);
        if (world.IsSolid(cell.X, cell.Y, cell.Z))
        {
            // Inside a solid cell (e.g. an item resting in a crevice): use the brightest open neighbour.
            foreach (var o in Faces.Offsets)
            {
                ushort v = Read(world, cell + o);
                if (LightValue.Sky(v) + LightValue.BlockMax(v) > LightValue.Sky(best) + LightValue.BlockMax(best)) best = v;
            }
        }
        return new Color(LightValue.R(best) / 15f, LightValue.G(best) / 15f, LightValue.B(best) / 15f, LightValue.Sky(best) / 15f);
    }

    private static ushort Read(VoxelWorld world, Int3 cell)
    {
        var chunk = world.GetChunk(ChunkMath.CellToChunk(cell));
        var light = chunk?.Light;
        if (light == null) return LightValue.Pack(15, 0, 0, 0);
        return light[ChunkMath.Index(cell.X & ChunkMath.Mask, cell.Y & ChunkMath.Mask, cell.Z & ChunkMath.Mask)];
    }
}
