using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 18 — decoration: grass and biome turf on exposed soil, moss on cave rock, trees and plants per
/// biome, stalagmites/stalactites and rubble in caves.
/// </summary>
public sealed class DecorationPass : GenPass
{
    private DecorPalette? _decor;

    public override string Name => "decoration";
    public override int Number => 18;

    public override void Plan(PlanContext ctx) => _decor = new DecorPalette(ctx.Content);

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        ApplySurfaceMaterials(ctx);
        PlaceTrees(ctx);
        PlacePlantsAndCaveDecor(ctx);
    }

    private void ApplySurfaceMaterials(ChunkGenContext ctx)
    {
        var p = ctx.Palette;
        var d = _decor!;
        ulong mossSeed = ctx.Plan.Seeds.Child("moss").Value;
        int n = ChunkGenContext.Size;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                float h = ctx.SurfaceHeight[ChunkGenContext.Column(lx, lz)];
                for (int ly = 0; ly < n - 1; ly++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    ushort b = ctx.Blocks[i];
                    if (b == p.Air || ctx.Blocks[ChunkGenContext.Index(lx, ly + 1, lz)] != p.Air) continue;
                    int wy = ctx.Origin.Y + ly;
                    byte biome = ctx.Biome[i];
                    bool nearSurface = wy >= h - 8f;
                    if (b == p.Loam && nearSurface)
                        ctx.Blocks[i] = biome == d.Umbral ? p.UmbralTurf : biome == d.Sanguine ? p.SanguineTurf : p.Turf;
                    else if (b == p.Mud && biome == d.Jungle) ctx.Blocks[i] = p.JungleTurf;
                    else if (b == p.Mud && biome == d.Mushroom) ctx.Blocks[i] = p.MushroomTurf;
                    else if ((b == p.Stone || b == p.Deepstone) && !nearSurface)
                    {
                        int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                        float mask = Hash.ToUnitFloat(Hash.Cell(mossSeed, wx >> 3, wy >> 3, wz >> 3));
                        if (mask < 0.12f) ctx.Blocks[i] = ctx.Depth(wy) > 0.5f && mask < 0.05f ? p.GlowMossStone : p.MossStone;
                    }
                }
            }
    }

    private void PlaceTrees(ChunkGenContext ctx)
    {
        const int grid = 9; // one candidate per 4.5 m square: open woods you can walk (and draw) comfortably
        var d = _decor!;
        ulong seed = ctx.Plan.Seeds.Child("trees").Value;
        int g0x = (int)MathF.Floor(ctx.ChunkOrigin.X / (float)grid), g0z = (int)MathF.Floor(ctx.ChunkOrigin.Z / (float)grid);
        for (int gz = g0z; gz <= g0z + ChunkMath.Size / grid + 1; gz++)
            for (int gx = g0x; gx <= g0x + ChunkMath.Size / grid + 1; gx++)
            {
                ulong h = Hash.Cell(seed, gx, gz);
                int x = gx * grid + 1 + Hash.ToRange(h, grid - 2), z = gz * grid + 1 + Hash.ToRange(Hash.Mix(h), grid - 2);
                if (x < ctx.ChunkOrigin.X + 1 || z < ctx.ChunkOrigin.Z + 1 || x >= ctx.ChunkOrigin.X + ChunkMath.Size - 2 || z >= ctx.ChunkOrigin.Z + ChunkMath.Size - 2) continue;
                if (!TryGround(ctx, x, z, out int y, out ushort ground)) continue;
                var (tree, chance) = d.TreeFor(ctx, ground, ctx.Biome[ctx.WorldIndex(x, y, z)], x, z);
                if (tree == null || Hash.ToUnitFloat(Hash.Mix(h + 7)) > chance) continue;
                int variant = Hash.ToRange(Hash.Mix(h + 11), tree.Variants);
                ctx.AddObject(new TileObjectInstance(tree, new Int3(x - tree.SizeX / 2, y + 1, z - tree.SizeZ / 2), Hash.ToRange(h, 4), variant));
            }
    }

    private void PlacePlantsAndCaveDecor(ChunkGenContext ctx)
    {
        var p = ctx.Palette;
        var d = _decor!;
        ulong seed = ctx.Plan.Seeds.Child("plants").Value;
        const int layer = ChunkGenContext.Size * ChunkGenContext.Size;
        var blocks = ctx.Blocks;
        var liquids = ctx.Liquids;
        for (int ly = 1; ly <= ChunkMath.Size; ly++)
            for (int lz = 1; lz <= ChunkMath.Size; lz++)
                for (int lx = 1; lx <= ChunkMath.Size; lx++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (blocks[i] != p.Air || (liquids != null && liquids[i] != 0)) continue;
                    ushort below = blocks[i - layer], above = blocks[i + layer];
                    if (below == p.Air && above == p.Air) continue;
                    int x = ctx.Origin.X + lx, y = ctx.Origin.Y + ly, z = ctx.Origin.Z + lz;
                    float roll = Hash.ToUnitFloat(Hash.Cell(seed, x, y, z));
                    TileObjectDef? pick = null;
                    if (below != p.Air) pick = d.FloorPlant(ctx, below, ctx.Biome[i], roll, ctx.Depth(y));
                    else if ((above == p.Stone || above == p.Deepstone) && roll < 0.008f) pick = d.Stalactite;
                    if (pick == null) continue;
                    int yy = pick.Anchor == TileAnchor.Ceiling ? y - pick.SizeY + 1 : y;
                    ctx.AddObject(new TileObjectInstance(pick, new Int3(x, yy, z), Hash.ToRange(Hash.Mix((ulong)x * 31 + (ulong)z), 4), Hash.ToRange(Hash.Mix((ulong)y), pick.Variants)));
                }
    }

    /// <summary>Topmost ground cell near the terrain surface in this chunk's column, with headroom above.</summary>
    private static bool TryGround(ChunkGenContext ctx, int x, int z, out int y, out ushort ground)
    {
        var p = ctx.Palette;
        float h = ctx.SurfaceHeight[ChunkGenContext.Column(x - ctx.Origin.X, z - ctx.Origin.Z)];
        for (y = ctx.ChunkOrigin.Y + ChunkMath.Size - 1; y >= ctx.ChunkOrigin.Y; y--)
        {
            ground = ctx.Get(x, y, z);
            if (ground == p.Air) continue;
            if (MathF.Abs(y - h) > 8f || ctx.Get(x, y + 1, z) != p.Air) return false;
            if (ctx.Liquids != null && ctx.Contains(x, y + 1, z) && ctx.Liquids[ctx.WorldIndex(x, y + 1, z)] != 0) return false;
            return true;
        }
        ground = p.Air;
        return false;
    }
}
