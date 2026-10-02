using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Chunk;

/// <summary>A cube of block ids in world space that veins are stamped into (a generating chunk or a live chunk's copy).</summary>
public interface IBlockBuffer
{
    /// <summary>World cell of the buffer's first cell.</summary>
    Int3 Origin { get; }

    /// <summary>Edge length in cells.</summary>
    int Size { get; }

    ushort Get(int wx, int wy, int wz);

    void Set(int wx, int wy, int wz, ushort block);
}

/// <summary>A vein rule resolved against content: block ids to place and replace, biome filter and its seed.</summary>
public sealed record VeinRule(OreVeinDef Def, ushort Block, HashSet<ushort> Replace, HashSet<byte> Biomes, ulong Seed);

/// <summary>
/// Places ore/gem/pocket veins. Veins are seeded per chunk and may cross chunk borders; a buffer evaluates the veins
/// of its chunk's 26 neighbours too, so results are seamless and independent of which chunk is processed first. Used
/// by world generation (pass 8) and by world mutations that add ores later (hardmode).
/// </summary>
public static class VeinStamper
{
    /// <summary>Stamps every vein of <paramref name="rule"/> seeded around <paramref name="chunk"/> that reaches the buffer.</summary>
    public static void Stamp<T>(ref T buf, WorldGenerator gen, VeinRule rule, Int3 chunk) where T : struct, IBlockBuffer
    {
        var bands = gen.Plan.Bands;
        float yTop = bands.YForNormalizedDepth(rule.Def.DepthMin);
        float yBottom = bands.YForNormalizedDepth(rule.Def.DepthMax);
        // Don't Dig Up: depth is not monotonic in y (it grows down under the bottom surface and up above the roof), so
        // each vein's depth is tested instead (no quick reject).
        if (!bands.Inverted && (buf.Origin.Y > yTop + 16 || buf.Origin.Y + buf.Size < yBottom - 16)) return;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    PlaceVeins(ref buf, gen, rule, chunk + new Int3(dx, dy, dz), yTop, yBottom);
    }

    private static void PlaceVeins<T>(ref T buf, WorldGenerator gen, VeinRule rule, Int3 source, float yTop, float yBottom) where T : struct, IBlockBuffer
    {
        var rng = new Pcg32(Hash.Cell(rule.Seed, source.X, source.Y, source.Z));
        int count = (int)rule.Def.PerChunk + (rng.NextFloat() < rule.Def.PerChunk % 1f ? 1 : 0);
        var origin = ChunkMath.ChunkOrigin(source);
        var o = buf.Origin;
        int size = buf.Size;
        for (int n = 0; n < count; n++)
        {
            float cx = origin.X + rng.NextFloat() * ChunkMath.Size;
            float cy = origin.Y + rng.NextFloat() * ChunkMath.Size;
            float cz = origin.Z + rng.NextFloat() * ChunkMath.Size;
            float r = rng.Range(rule.Def.SizeMin, rule.Def.SizeMax);
            float heading = rng.NextFloat();
            float tilt = rng.Range(-0.3f, 0.3f);
            uint salt = rng.NextUInt(); // draw every random value before any chunk-dependent rejection
            if (gen.Plan.Bands.Inverted ? !InDepth(gen.Plan.Bands, cy, rule) : cy > yTop || cy < yBottom) continue;
            float reach = r * rule.Def.Stretch + 1f;
            if (cx + reach < o.X || cx - reach > o.X + size
                || cy + reach < o.Y || cy - reach > o.Y + size
                || cz + reach < o.Z || cz - reach > o.Z + size) continue;
            if (rule.Biomes.Count > 0 && !rule.Biomes.Contains(gen.Biomes.BiomeAt(cx, cy, cz))) continue;
            Place(ref buf, rule, cx, cy, cz, r, heading, tilt, salt);
        }
    }

    private static bool InDepth(Plan.LayerBands bands, float y, VeinRule rule)
    {
        float d = bands.NormalizedDepth(y);
        return d >= rule.Def.DepthMin && d <= rule.Def.DepthMax;
    }

    private static void Place<T>(ref T buf, VeinRule rule, float cx, float cy, float cz, float r, float heading, float tilt, uint salt) where T : struct, IBlockBuffer
    {
        float stretch = rule.Def.Stretch;
        float reach = r * stretch + 1f;
        var min = new Int3((int)MathF.Floor(cx - reach), (int)MathF.Floor(cy - reach), (int)MathF.Floor(cz - reach));
        var max = new Int3((int)MathF.Ceiling(cx + reach) + 1, (int)MathF.Ceiling(cy + reach) + 1, (int)MathF.Ceiling(cz + reach) + 1);
        float ax = Trig.Cos(heading), az = Trig.Sin(heading), ay = tilt;
        float alen = MathF.Sqrt(ax * ax + ay * ay + az * az);
        ax /= alen; ay /= alen; az /= alen;
        var box = new Aabb3i(buf.Origin, buf.Origin + Int3.One * buf.Size).Intersection(new Aabb3i(min, max));
        if (box.IsEmpty) return;
        float limit = (r + 0.8f) * (r + 0.8f);
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                {
                    if (!rule.Replace.Contains(buf.Get(x, y, z))) continue;
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                    float along = dx * ax + dy * ay + dz * az;
                    float px = dx - along * ax, py = dy - along * ay, pz = dz - along * az;
                    float d2 = (along / stretch) * (along / stretch) + px * px + py * py + pz * pz;
                    if (d2 > limit) continue;
                    float rough = Hash.ToUnitFloat(Hash.Cell(salt, x, y, z)) * 0.8f;
                    if (d2 < (r + rough) * (r + rough) * 0.8f) buf.Set(x, y, z, rule.Block);
                }
    }
}
