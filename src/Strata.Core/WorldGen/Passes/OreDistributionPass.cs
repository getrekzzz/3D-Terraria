using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 8 — ore, gem and material-pocket veins driven entirely by <c>oreVeins</c> content data.
/// Veins are seeded per chunk and may cross chunk borders; each chunk evaluates the veins of its 26
/// neighbours too, so results are seamless and independent of generation order.
/// </summary>
public sealed class OreDistributionPass : GenPass
{
    private readonly List<Vein> _veins = new();

    public override string Name => "ores";
    public override int Number => 8;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("alternates");
        foreach (var group in ctx.Content.OreVeins.All.Where(v => v.Group != null).GroupBy(v => v.Group!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var options = group.OrderBy(v => v.Id, StringComparer.Ordinal).ToList();
            ctx.Plan.OreChoices[group.Key] = options[rng.NextInt(options.Count)].Id;
        }

        foreach (var def in ctx.Content.OreVeins.All)
        {
            if (def.Group != null && ctx.Plan.OreChoices[def.Group] != def.Id) continue;
            _veins.Add(new Vein(
                def,
                (ushort)ctx.Content.Blocks.Get(def.Block).RuntimeId,
                def.Replace.Select(r => (ushort)ctx.Content.Blocks.Get(r).RuntimeId).ToHashSet(),
                def.Biomes.Select(b => (byte)ctx.Content.Biomes.Get(b).RuntimeId).ToHashSet(),
                ctx.SeedsFor(this).Child(def.Id).Value));
        }
    }

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        var bands = ctx.Plan.Bands;
        foreach (var vein in _veins)
        {
            float yTop = bands.YForNormalizedDepth(vein.Def.DepthMin);
            float yBottom = bands.YForNormalizedDepth(vein.Def.DepthMax);
            if (ctx.Origin.Y > yTop + 16 || ctx.Origin.Y + ChunkGenContext.Size < yBottom - 16) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        PlaceVeins(ctx, vein, ctx.Coord + new Int3(dx, dy, dz), yTop, yBottom);
        }
    }

    private static void PlaceVeins(ChunkGenContext ctx, Vein vein, Int3 source, float yTop, float yBottom)
    {
        var rng = new Pcg32(Hash.Cell(vein.Seed, source.X, source.Y, source.Z));
        int count = (int)vein.Def.PerChunk + (rng.NextFloat() < vein.Def.PerChunk % 1f ? 1 : 0);
        var origin = ChunkMath.ChunkOrigin(source);
        for (int n = 0; n < count; n++)
        {
            float cx = origin.X + rng.NextFloat() * ChunkMath.Size;
            float cy = origin.Y + rng.NextFloat() * ChunkMath.Size;
            float cz = origin.Z + rng.NextFloat() * ChunkMath.Size;
            float r = rng.Range(vein.Def.SizeMin, vein.Def.SizeMax);
            float heading = rng.NextFloat();
            float tilt = rng.Range(-0.3f, 0.3f);
            uint salt = rng.NextUInt(); // draw every random value before any chunk-dependent rejection
            if (cy > yTop || cy < yBottom) continue;
            float reach = r * vein.Def.Stretch + 1f;
            if (cx + reach < ctx.Origin.X || cx - reach > ctx.Origin.X + ChunkGenContext.Size
                || cy + reach < ctx.Origin.Y || cy - reach > ctx.Origin.Y + ChunkGenContext.Size
                || cz + reach < ctx.Origin.Z || cz - reach > ctx.Origin.Z + ChunkGenContext.Size) continue;
            if (vein.Biomes.Count > 0 && !vein.Biomes.Contains(ctx.Gen.Biomes.BiomeAt(cx, cy, cz))) continue;
            Stamp(ctx, vein, cx, cy, cz, r, heading, tilt, salt);
        }
    }

    private static void Stamp(ChunkGenContext ctx, Vein vein, float cx, float cy, float cz, float r, float heading, float tilt, uint salt)
    {
        float stretch = vein.Def.Stretch;
        float reach = r * stretch + 1f;
        var min = new Int3((int)MathF.Floor(cx - reach), (int)MathF.Floor(cy - reach), (int)MathF.Floor(cz - reach));
        var max = new Int3((int)MathF.Ceiling(cx + reach) + 1, (int)MathF.Ceiling(cy + reach) + 1, (int)MathF.Ceiling(cz + reach) + 1);
        float ax = Trig.Cos(heading), az = Trig.Sin(heading), ay = tilt;
        float alen = MathF.Sqrt(ax * ax + ay * ay + az * az);
        ax /= alen; ay /= alen; az /= alen;
        var box = new Aabb3i(ctx.Origin, ctx.Origin + Int3.One * ChunkGenContext.Size).Intersection(new Aabb3i(min, max));
        if (box.IsEmpty) return;
        float limit = (r + 0.8f) * (r + 0.8f);
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                {
                    int i = ctx.WorldIndex(x, y, z);
                    if (!vein.Replace.Contains(ctx.Blocks[i])) continue;
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, dz = z + 0.5f - cz;
                    float along = dx * ax + dy * ay + dz * az;
                    float px = dx - along * ax, py = dy - along * ay, pz = dz - along * az;
                    float d2 = (along / stretch) * (along / stretch) + px * px + py * py + pz * pz;
                    if (d2 > limit) continue;
                    float rough = Hash.ToUnitFloat(Hash.Cell(salt, x, y, z)) * 0.8f;
                    if (d2 < (r + rough) * (r + rough) * 0.8f) ctx.Blocks[i] = vein.Block;
                }
    }

    private sealed record Vein(OreVeinDef Def, ushort Block, HashSet<ushort> Replace, HashSet<byte> Biomes, ulong Seed);
}
