using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>
/// A colossal hollow heartwood tree: a surface landmark whose hollow trunk descends through the ground
/// into a root chamber holding treasure.
/// </summary>
public static class LivingTreeBuilder
{
    public static Blueprint Build(ContentDatabase content, Int3 groundCenter, ref Pcg32 rng)
    {
        var bp = new Blueprint { Flags = Voxels.CellFlags.None };
        float x = groundCenter.X + 0.5f, z = groundCenter.Z + 0.5f;
        int ground = groundCenter.Y;
        float radius = rng.Range(4.5f, 6f);
        int height = rng.Range(44, 60);
        int rootDepth = rng.Range(28, 44);

        bp.Cylinder(x, z, radius + 1.5f, ground - 6, ground + 3, "living_wood");
        bp.Cylinder(x, z, radius, ground, ground + height, "living_wood");
        for (int i = 0; i < 4; i++)
        {
            float a = i * 0.25f + rng.Range(-0.05f, 0.05f);
            var tip = new Vector3(x + Trig.Cos(a) * radius * 2.6f, ground + height - rng.Range(6, 14), z + Trig.Sin(a) * radius * 2.6f);
            bp.Ellipsoid(tip, new Vector3(radius * 1.8f, 2.2f, radius * 1.8f), "living_wood");
            bp.Ellipsoid(tip + new Vector3(0, 5, 0), new Vector3(rng.Range(9f, 12f), rng.Range(5f, 7f), rng.Range(9f, 12f)), "leaf_block", onlyAir: true);
        }
        bp.Ellipsoid(new Vector3(x, ground + height + 4, z), new Vector3(rng.Range(15f, 19f), rng.Range(8f, 11f), rng.Range(15f, 19f)), "leaf_block", onlyAir: true);

        // Hollow core from the canopy to the root chamber.
        bp.Cylinder(x, z, radius - 2f, ground - rootDepth, ground + height - 4, null);
        var chamber = new Vector3(x, ground - rootDepth, z);
        bp.Ellipsoid(chamber, new Vector3(11, 7, 11), "living_wood");
        bp.Ellipsoid(chamber, new Vector3(9.5f, 5.5f, 9.5f), null);
        var floor = new Int3((int)x - 1, ground - rootDepth - 4, (int)z + 3);

        // Platforms up the hollow from the chamber floor, alternating sides 4 cells apart: each is one jump from the
        // last with full headroom above it, so a player who falls in can always climb back out.
        int side = 0;
        for (int y = floor.Y + 3; y < ground + height - 6; y += 4, side ^= 1)
        {
            int x0 = side == 0 ? (int)x - 3 : (int)x + 1;
            bp.Fill(new Aabb3i(new Int3(x0, y, (int)z - 1), new Int3(x0 + 3, y + 1, (int)z + 2)), "wood", Voxels.ShapeCode.Pack(Voxels.CellShape.Platform, 0));
        }
        bp.Carve(new Aabb3i(new Int3((int)(x + radius) - 1, ground, (int)z - 1), new Int3((int)(x + radius) + 3, ground + 5, (int)z + 2)));

        bp.Fill(new Aabb3i(floor - new Int3(6, 1, 9), floor + new Int3(8, 0, 4)), "living_wood");
        bp.Object(content.TileObjects.Get("wooden_chest"), floor, 0, 0, "cabin_chest");
        bp.Object(content.TileObjects.Get("torch"), floor + new Int3(4, 0, 0), 0);
        return bp;
    }
}
