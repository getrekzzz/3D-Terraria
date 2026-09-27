using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Passes;

/// <summary>Carves ellipsoidal chambers with kind-specific interiors (lakes, geodes, rubble, pillars…).</summary>
internal static class ChamberCarver
{
    public static void Carve(ChunkGenContext ctx, ChamberFeature ch)
    {
        var p = ctx.Palette;
        ulong seed = ch.Seed;
        FeatureCarver.ForEachCell(ctx, ch.Bounds, (x, y, z, i) =>
        {
            if (ctx.Blocks[i] == p.Bedrock) return;
            float d = ch.Distance(x + 0.5f, y + 0.5f, z + 0.5f);
            float jitter = (Hash.ToUnitFloat(Hash.Cell(seed, x >> 2, y >> 2, z >> 2)) - 0.5f) * 0.12f;
            d += jitter;
            switch (ch.Chamber)
            {
                case ChamberKind.Geode:
                    if (d < 0.72f) ctx.Blocks[i] = p.Air;
                    else if (d < 1f) ctx.Blocks[i] = p.Crystal;
                    else if (d < 1.18f && ctx.Blocks[i] != p.Air) ctx.Blocks[i] = p.Obsidian;
                    break;
                case ChamberKind.Lake:
                    if (d < 1f)
                    {
                        ctx.Blocks[i] = p.Air;
                        if (y + 0.5f < ch.Center.Y - ch.Radii.Y * 0.1f) ctx.EnsureLiquids()[i] = LiquidCode.Full(LiquidType.Water);
                    }
                    break;
                case ChamberKind.Collapsed:
                    if (d < 1f)
                    {
                        float floor = ch.Center.Y - ch.Radii.Y * 0.55f;
                        float mound = Hash.ToUnitFloat(Hash.Cell(seed, x >> 3, 0, z >> 3)) * ch.Radii.Y * 0.7f;
                        ctx.Blocks[i] = y < floor + mound * (1f - d) ? p.Stone : p.Air;
                    }
                    break;
                case ChamberKind.Grand:
                    if (d < 1f && !IsPillar(seed, x, z, ch)) ctx.Blocks[i] = p.Air;
                    break;
                case ChamberKind.Mushroom:
                    if (d < 1f) ctx.Blocks[i] = p.Air;
                    else if (d < 1.2f && ctx.Blocks[i] != p.Air) ctx.Blocks[i] = p.Mud;
                    break;
                default:
                    if (d < 1f) ctx.Blocks[i] = p.Air;
                    break;
            }
        });
    }

    /// <summary>Massive stone columns holding up grand caverns.</summary>
    private static bool IsPillar(ulong seed, int x, int z, ChamberFeature ch)
    {
        const int cell = 24;
        int gx = (int)MathF.Floor(x / (float)cell), gz = (int)MathF.Floor(z / (float)cell);
        ulong h = Hash.Cell(seed ^ 0x9E37UL, gx, gz);
        if (Hash.ToUnitFloat(h) > 0.35f) return false;
        float px = gx * cell + 6 + Hash.ToUnitFloat(Hash.Mix(h)) * (cell - 12);
        float pz = gz * cell + 6 + Hash.ToUnitFloat(Hash.Mix(h + 1)) * (cell - 12);
        float r = 3f + Hash.ToUnitFloat(Hash.Mix(h + 2)) * 4f;
        float dx = x + 0.5f - px, dz = z + 0.5f - pz;
        _ = ch;
        return dx * dx + dz * dz < r * r;
    }
}
