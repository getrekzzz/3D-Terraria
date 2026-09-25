using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures;

/// <summary>A stepped sandstone pyramid with a sealed descent to a treasure vault beneath the sands.</summary>
public static class PyramidBuilder
{
    public static Blueprint Build(ContentDatabase content, Int3 groundCenter, ref Pcg32 rng)
    {
        var bp = new Blueprint();
        int half = rng.Range(22, 30);
        int ground = groundCenter.Y - 2;
        for (int layer = 0; half - layer * 2 > 2; layer++)
        {
            int r = half - layer * 2;
            int y = ground + layer * 2;
            bp.Fill(new Aabb3i(new Int3(groundCenter.X - r, y, groundCenter.Z - r), new Int3(groundCenter.X + r, y + 2, groundCenter.Z + r)), "sandstone_brick");
        }

        // Entrance tunnel from the -X face sloping into the core, then a shaft to the vault.
        int ey = ground + 3;
        bp.Carve(new Aabb3i(new Int3(groundCenter.X - half - 1, ey, groundCenter.Z - 2), new Int3(groundCenter.X + 1, ey + 5, groundCenter.Z + 2)));
        int vaultY = ground - rng.Range(26, 40);
        bp.Carve(new Aabb3i(new Int3(groundCenter.X - 2, vaultY + 6, groundCenter.Z - 2), new Int3(groundCenter.X + 2, ey + 1, groundCenter.Z + 2)));
        var vault = new Aabb3i(new Int3(groundCenter.X - 9, vaultY - 1, groundCenter.Z - 9), new Int3(groundCenter.X + 9, vaultY + 8, groundCenter.Z + 9));
        bp.Room(vault, "sandstone_brick");
        bp.Carve(new Aabb3i(new Int3(groundCenter.X - 2, vaultY + 6, groundCenter.Z - 2), new Int3(groundCenter.X + 2, vaultY + 8, groundCenter.Z + 2)));

        var tiles = content.TileObjects;
        bp.Object(tiles.Get("gold_chest"), new Int3(groundCenter.X - 1, vaultY, groundCenter.Z + 5), 0, 0, "cabin_chest");
        for (int i = 0; i < 4; i++)
            bp.Object(tiles.Get("clay_pot"), new Int3(groundCenter.X - 7 + i * 4, vaultY, groundCenter.Z - 7), 0, i % 3);
        bp.Object(tiles.Get("torch"), new Int3(groundCenter.X + 6, vaultY, groundCenter.Z + 6), 0);
        return bp;
    }
}
