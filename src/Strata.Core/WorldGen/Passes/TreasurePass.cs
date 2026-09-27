using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 16 — treasure: breakable pots on cave floors, chests in the caves (Gold Chests in the cavern layer) and vital
/// crystals in the deep, placed only where the object genuinely fits (solid floor, free headroom).
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
        var pot = tiles.Get("pot");
        var chest = tiles.Get("wooden_chest");
        var goldChest = tiles.Get("gold_chest");
        var crystal = tiles.Get("life_crystal_node");
        var altar = tiles.Get(ctx.Gen.Plan.Settings.Evil == World.EvilKind.Crimson ? "crimson_altar" : "demon_altar");
        var bulb = tiles.Get("plantera_bulb");
        byte jungle = (byte)ctx.Gen.Content.Biomes.Get("jungle").RuntimeId;
        float depth = ctx.Depth(ctx.ChunkOrigin.Y + 16);
        if (depth < 0.02f) return;

        for (int attempt = 0; attempt < 28; attempt++)
        {
            int x = ctx.ChunkOrigin.X + rng.NextInt(ChunkMath.Size - 2);
            int z = ctx.ChunkOrigin.Z + rng.NextInt(ChunkMath.Size - 2);
            if (!TryFindFloor(ctx, x, z, 3, out int floorY)) continue;
            float roll = rng.NextFloat();
            if (roll < 0.018f + 0.02f * depth) Place(ctx, crystal, x, floorY, z, 0, null);
            else if (roll < 0.05f)
            {
                // Chests hidden in the caves: wooden ones near the surface, Gold Chests in the cavern layer, often with
                // a few pots around them.
                bool deep = depth > 0.4f;
                Place(ctx, deep ? goldChest : chest, x, floorY, z, rng.NextInt(4), deep ? "gold_chest" : "surface_chest");
                for (int k = rng.Range(0, 3); k > 0; k--)
                {
                    int px = Math.Clamp(x + rng.Range(-4, 5), ctx.ChunkOrigin.X, ctx.ChunkOrigin.X + ChunkMath.Size - 3);
                    int pz = Math.Clamp(z + rng.Range(-4, 5), ctx.ChunkOrigin.Z, ctx.ChunkOrigin.Z + ChunkMath.Size - 3);
                    if (Math.Abs(px - x) < 3 && Math.Abs(pz - z) < 3) continue;
                    if (TryFindFloor(ctx, px, pz, 3, out int py) && Math.Abs(py - floorY) <= 2) Place(ctx, pot, px, py, pz, rng.NextInt(4), null, rng.NextInt(3));
                }
            }
            else if (roll < 0.17f) Place(ctx, pot, x, floorY, z, rng.NextInt(4), null, rng.NextInt(3));
            else if (roll < 0.1745f && depth > 0.06f && TryFindFloor(ctx, x + 2, z, 3, out int other) && other == floorY)
                Place(ctx, altar, x, floorY, z, rng.NextInt(4), null); // demon altars dot the underground
            else if (roll < 0.182f && depth > 0.3f && ctx.Biome[ctx.WorldIndex(x, floorY, z)] == jungle)
                Place(ctx, bulb, x, floorY, z, 0, null); // Plantera's Bulbs, sealed until the mechanical bosses fall
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
