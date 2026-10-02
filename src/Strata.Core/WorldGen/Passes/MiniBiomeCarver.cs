using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>Interiors for mini-biome chambers: marble halls with columns, granite geode rooms, shrines, spider nests, hives.</summary>
internal static class MiniBiomeCarver
{
    public static void Carve(ChunkGenContext ctx, ChamberFeature ch)
    {
        var p = ctx.Palette;
        ushort wall = ch.Chamber switch
        {
            ChamberKind.Marble => p.Marble,
            ChamberKind.Granite => p.Granite,
            ChamberKind.Hive => p.Hive,
            ChamberKind.Shrine => p.MossStone,
            _ => p.Stone,
        };
        FeatureCarver.ForEachCell(ctx, ch.Bounds, (x, y, z, i) =>
        {
            if (ctx.Blocks[i] == p.Bedrock || ctx.IsStructure(i)) return;
            float d = ch.Distance(x + 0.5f, y + 0.5f, z + 0.5f);
            float jitter = (Hash.ToUnitFloat(Hash.Cell(ch.Seed, x >> 1, y >> 1, z >> 1)) - 0.5f) * 0.1f;
            d += jitter;
            if (d >= 1.25f) return;
            if (ch.Chamber == ChamberKind.Hive)
            {
                if (d < 0.82f)
                {
                    ctx.Blocks[i] = p.Air;
                    if (y + 0.5f < ch.Center.Y - ch.Radii.Y * 0.35f) ctx.EnsureLiquids()[i] = Voxels.LiquidCode.Full(Voxels.LiquidType.Honey);
                }
                else ctx.Blocks[i] = p.Hive;
                return;
            }
            if (ch.Chamber == ChamberKind.Aether)
            {
                // A dome over an aetherium floor, with a round Shimmer pool sunk into the middle of it.
                float floor = ch.Center.Y - ch.Radii.Y * 0.35f, cy = y + 0.5f;
                float hx = (x + 0.5f - ch.Center.X) / ch.Radii.X, hz = (z + 0.5f - ch.Center.Z) / ch.Radii.Z;
                bool pool = hx * hx + hz * hz < 0.45f * 0.45f && cy < floor && cy >= floor - 5f;
                if ((d < 0.85f && cy >= floor) || pool)
                {
                    ctx.Blocks[i] = p.Air;
                    if (pool) ctx.EnsureLiquids()[i] = Voxels.LiquidCode.Full(Voxels.LiquidType.Shimmer);
                }
                else if (d < 1.05f) ctx.Blocks[i] = p.Aetherium;
                return;
            }
            if (ch.Chamber == ChamberKind.SpiderNest)
            {
                if (d < 0.8f) ctx.Blocks[i] = p.Air;
                else if (d < 1f) ctx.Blocks[i] = Hash.ToUnitFloat(Hash.Cell(ch.Seed ^ 7, x, y, z)) < 0.55f ? p.Cobweb : p.Air;
                return;
            }
            if (d < 0.85f)
            {
                bool column = ch.Chamber == ChamberKind.Marble && IsColumn(ch, x, z);
                ctx.Blocks[i] = column ? p.Marble : p.Air;
            }
            else if (ctx.Blocks[i] != p.Air || d < 1f)
            {
                ctx.Blocks[i] = wall;
            }
        });
    }

    /// <summary>Regularly spaced fluted columns in marble halls.</summary>
    private static bool IsColumn(ChamberFeature ch, int x, int z)
    {
        int gx = (int)MathF.Floor((x - ch.Center.X) / 9f), gz = (int)MathF.Floor((z - ch.Center.Z) / 9f);
        float cx = ch.Center.X + gx * 9f + 4.5f, cz = ch.Center.Z + gz * 9f + 4.5f;
        float dx = x + 0.5f - cx, dz = z + 0.5f - cz;
        return dx * dx + dz * dz < 2.3f * 2.3f;
    }
}
