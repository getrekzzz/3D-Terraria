using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Crafting;

/// <summary>Finds crafting stations and liquids within reach of a position (the crafting environment).</summary>
public static class StationScanner
{
    /// <summary>Horizontal reach in cells (~3 m) and vertical reach (~2 m), like the reference game's station range.</summary>
    public const int ReachXZ = 6;
    public const int ReachY = 4;

    public static HashSet<string> Stations(VoxelWorld world, Int3 cell)
    {
        var box = new Aabb3i(cell - new Int3(ReachXZ, ReachY, ReachXZ), cell + new Int3(ReachXZ + 1, ReachY + 1, ReachXZ + 1));
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in world.TileObjects.InBox(box))
            foreach (var s in obj.Def.Stations) set.Add(s);
        return set;
    }

    public static HashSet<string> Liquids(VoxelWorld world, Int3 cell)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int y = -ReachY; y <= ReachY; y++)
            for (int z = -ReachXZ; z <= ReachXZ; z++)
                for (int x = -ReachXZ; x <= ReachXZ; x++)
                {
                    byte l = world.GetLiquid(cell.X + x, cell.Y + y, cell.Z + z);
                    if (l != 0 && world.Content.LiquidOf(l) is { } liquid) set.Add(liquid.Id);
                }
        return set;
    }
}
