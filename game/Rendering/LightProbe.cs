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

    /// <summary>
    /// Light at a point blended between the eight surrounding cell centres (open cells only), so a moving body
    /// brightens and darkens smoothly instead of stepping at every cell boundary.
    /// </summary>
    public static Color SampleSmooth(VoxelWorld world, Vector3 meters)
    {
        var p = meters / Units.CellSize - Vector3.One * 0.5f;
        int x0 = Mathf.FloorToInt(p.X), y0 = Mathf.FloorToInt(p.Y), z0 = Mathf.FloorToInt(p.Z);
        float fx = p.X - x0, fy = p.Y - y0, fz = p.Z - z0;
        var sum = new Color(0, 0, 0, 0);
        float weight = 0;
        for (int i = 0; i < 8; i++)
        {
            int dx = i & 1, dy = (i >> 1) & 1, dz = i >> 2;
            if (world.IsSolid(x0 + dx, y0 + dy, z0 + dz)) continue;
            float w = (dx == 1 ? fx : 1 - fx) * (dy == 1 ? fy : 1 - fy) * (dz == 1 ? fz : 1 - fz);
            if (w <= 0) continue;
            ushort v = Read(world, new Int3(x0 + dx, y0 + dy, z0 + dz));
            sum += new Color(LightValue.R(v), LightValue.G(v), LightValue.B(v), LightValue.Sky(v)) * w;
            weight += w;
        }
        return weight > 1e-4f ? sum / (weight * 15f) : Sample(world, meters);
    }

    private static ushort Read(VoxelWorld world, Int3 cell)
    {
        var chunk = world.GetChunk(ChunkMath.CellToChunk(cell));
        var light = chunk?.Light;
        if (light == null) return LightValue.Pack(15, 0, 0, 0);
        return light[ChunkMath.Index(cell.X & ChunkMath.Mask, cell.Y & ChunkMath.Mask, cell.Z & ChunkMath.Mask)];
    }
}
