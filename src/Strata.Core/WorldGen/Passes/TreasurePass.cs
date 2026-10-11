using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Caves;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 16 — treasure: the chests and pots the cave network planned in its rooms, then a scattering of breakable pots on
/// cave floors, chests hidden in the caves (the biome's wooden-tier chest higher up, Gold Chests deeper) and vital
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
        // A Drunk World has both kinds of altar (chunk by chunk).
        bool crimson = ctx.Gen.Plan.Settings.Has(World.SecretSeed.Drunk)
            ? ((ctx.ChunkOrigin.X + ctx.ChunkOrigin.Z) / Common.ChunkMath.Size & 1) == 1
            : ctx.Gen.Plan.Settings.Evil == World.EvilKind.Crimson;
        var altar = tiles.Get(crimson ? "crimson_altar" : "demon_altar");
        var bulb = tiles.Get("plantera_bulb");
        byte jungle = (byte)ctx.Gen.Content.Biomes.Get("jungle").RuntimeId;
        PlaceRoomTreasure(ctx);
        float depth = ctx.Depth(ctx.ChunkOrigin.Y + 16);
        if (depth < 0.02f) return;
        // Most chests and many pots now stand in the cave network's rooms, so the random ones are rarer than they were.
        float crystalUpTo = 0.018f + 0.02f * depth, chestUpTo = crystalUpTo + (0.05f - crystalUpTo) * 0.35f;
        float potUpTo = chestUpTo + 0.066f, altarUpTo = potUpTo + 0.0045f, bulbUpTo = altarUpTo + 0.0075f;

        for (int attempt = 0; attempt < 28; attempt++)
        {
            int x = ctx.ChunkOrigin.X + rng.NextInt(ChunkMath.Size - 2);
            int z = ctx.ChunkOrigin.Z + rng.NextInt(ChunkMath.Size - 2);
            if (!TryFindFloor(ctx, x, z, 3, out int floorY) || ctx.IsStructure(x, floorY - 1, z)) continue; // not on structure floors
            float roll = rng.NextFloat();
            if (roll < crystalUpTo) Place(ctx, crystal, x, floorY, z, 0, null);
            else if (roll < chestUpTo)
            {
                // Chests hidden in the caves: the biome's wooden-tier chest higher up, Gold Chests deeper, often with a few
                // pots around them.
                bool deep = depth > CaveChests.GoldDepth;
                var wooden = deep ? goldChest : tiles.Get(CaveChests.WoodenFor(ctx.Gen.Biomes.IdOf(ctx.Biome[ctx.WorldIndex(x, floorY, z)])));
                Place(ctx, wooden, x, floorY, z, rng.NextInt(4), deep ? "gold_chest" : "cave_chest");
                for (int k = rng.Range(0, 3); k > 0; k--)
                {
                    int px = Math.Clamp(x + rng.Range(-4, 5), ctx.ChunkOrigin.X, ctx.ChunkOrigin.X + ChunkMath.Size - 3);
                    int pz = Math.Clamp(z + rng.Range(-4, 5), ctx.ChunkOrigin.Z, ctx.ChunkOrigin.Z + ChunkMath.Size - 3);
                    if (Math.Abs(px - x) < 3 && Math.Abs(pz - z) < 3) continue;
                    if (TryFindFloor(ctx, px, pz, 3, out int py) && Math.Abs(py - floorY) <= 2 && !ctx.IsStructure(px, py - 1, pz)) Place(ctx, pot, px, py, pz, rng.NextInt(4), null, rng.NextInt(3));
                }
            }
            else if (roll < potUpTo) Place(ctx, pot, x, floorY, z, rng.NextInt(4), null, rng.NextInt(3));
            else if (roll < altarUpTo && depth > 0.06f && TryFindFloor(ctx, x + 2, z, 3, out int other) && other == floorY && !ctx.IsStructure(x + 2, other - 1, z))
                Place(ctx, altar, x, floorY, z, rng.NextInt(4), null); // demon altars dot the underground
            else if (roll < bulbUpTo && depth > 0.3f && ctx.Biome[ctx.WorldIndex(x, floorY, z)] == jungle)
                Place(ctx, bulb, x, floorY, z, 0, null); // Plantera's Bulbs, sealed until the mechanical bosses fall
        }
    }

    /// <summary>
    /// Stands the treasure the cave network planned in its rooms (each object belongs to the chunk holding its anchor) on
    /// the floor: at the planned row, or the nearest row within a few that fits, should another pass have changed it.
    /// </summary>
    private static void PlaceRoomTreasure(ChunkGenContext ctx)
    {
        var tiles = ctx.Gen.Content.TileObjects;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is not CaveRoomFeature room) continue;
            foreach (var t in room.Treasure)
            {
                var a = t.Anchor;
                if (!ctx.InCore(a.X, a.Y, a.Z) || !tiles.TryGet(t.Object, out var def)) continue;
                for (int k = 0; k < 7; k++)
                {
                    int dy = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
                    var at = new TileObjectInstance(def, a + new Int3(0, dy, 0), t.Rotation, t.Variant) { PendingLoot = t.Loot };
                    if (Fits(ctx, at.Bounds) && ctx.AddObject(at)) break;
                }
            }
        }
    }

    /// <summary>Whether an object's box is open (no rock, liquid or structure) and the whole of it rests on solid ground.</summary>
    private static bool Fits(ChunkGenContext ctx, Aabb3i box)
    {
        var p = ctx.Palette;
        for (int z = box.Min.Z; z < box.Max.Z; z++)
            for (int x = box.Min.X; x < box.Max.X; x++)
            {
                if (!ctx.Contains(x, box.Min.Y - 1, z) || ctx.Get(x, box.Min.Y - 1, z) == p.Air || ctx.IsStructure(x, box.Min.Y - 1, z)) return false;
                for (int y = box.Min.Y; y < box.Max.Y; y++)
                {
                    if (!ctx.Contains(x, y, z) || ctx.Get(x, y, z) != p.Air) return false;
                    if (ctx.Liquids != null && ctx.Liquids[ctx.WorldIndex(x, y, z)] != 0) return false;
                }
            }
        return true;
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
