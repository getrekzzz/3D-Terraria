using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 16 — treasure: breakable pots on cave floors, buried chests in caverns and vital crystals in the
/// deep, placed only where the object genuinely fits (solid floor, free headroom).
/// </summary>
public sealed class TreasurePass : GenPass
{
    public override string Name => "treasure";
    public override int Number => 16;

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        var rng = ctx.Rng(this);
        var tiles = ctx.Gen.Content.TileObjects;
        var pot = tiles.Get("clay_pot");
        var chest = tiles.Get("wooden_chest");
        var crystal = tiles.Get("vital_crystal_node");
        float depth = ctx.Depth(ctx.ChunkOrigin.Y + 16);
        if (depth < 0.02f) return;

        for (int attempt = 0; attempt < 28; attempt++)
        {
            int x = ctx.ChunkOrigin.X + rng.NextInt(ChunkMath.Size - 2);
            int z = ctx.ChunkOrigin.Z + rng.NextInt(ChunkMath.Size - 2);
            if (!TryFindFloor(ctx, x, z, 3, out int floorY)) continue;
            float roll = rng.NextFloat();
            if (roll < 0.018f + 0.02f * depth) Place(ctx, crystal, x, floorY, z, 0, null);
            else if (roll < 0.05f) Place(ctx, chest, x, floorY, z, rng.NextInt(4), depth > 0.4f ? "cabin_chest" : "surface_chest");
            else if (roll < 0.17f) Place(ctx, pot, x, floorY, z, rng.NextInt(4), null, rng.NextInt(3));
        }
    }

    private static void Place(ChunkGenContext ctx, Content.Defs.TileObjectDef def, int x, int y, int z, int rotation, string? loot, int variant = 0) =>
        ctx.AddObject(new TileObjectInstance(def, new Int3(x, y, z), rotation, variant) { PendingLoot = loot });

    /// <summary>Scans down a column (inside the core) for an air cell with solid ground below and free headroom.</summary>
    internal static bool TryFindFloor(ChunkGenContext ctx, int x, int z, int headroom, out int floorY)
    {
        var p = ctx.Palette;
        for (int y = ctx.ChunkOrigin.Y + ChunkMath.Size - headroom; y >= ctx.ChunkOrigin.Y; y--)
        {
            if (ctx.Get(x, y, z) != p.Air || ctx.Get(x, y - 1, z) == p.Air) continue;
            if (ctx.Liquids != null && ctx.Liquids[ctx.WorldIndex(x, y, z)] != 0) continue;
            bool clear = true;
            for (int h = 1; h < headroom && clear; h++) clear = ctx.Get(x, y + h, z) == p.Air && ctx.Get(x + 1, y + h - 1, z + 1) == p.Air;
            if (!clear) continue;
            floorY = y;
            return true;
        }
        floorY = 0;
        return false;
    }
}
