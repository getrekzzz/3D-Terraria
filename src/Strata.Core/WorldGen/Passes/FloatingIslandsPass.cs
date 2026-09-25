using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.World;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Features;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 14 — floating islands: real landmasses hanging in the sky band, visible from the ground,
/// wreathed in cloud, some cradling cloud lakes, each crowned with a skystone shrine-house and chest.
/// </summary>
public sealed class FloatingIslandsPass : GenPass
{
    private NoiseField? _shape;

    public override string Name => "islands";
    public override int Number => 14;

    public override void Plan(PlanContext ctx)
    {
        var seeds = ctx.SeedsFor(this);
        _shape = NoiseField.Create(seeds, "shape", 22f, NoiseFractal.Fbm, 3);
        var rng = seeds.Rng("placement");
        var bands = ctx.Plan.Bands;
        int count = ctx.Plan.Settings.Size switch { WorldSizePreset.Tiny => 1, WorldSizePreset.Small => 4, WorldSizePreset.Medium => 6, _ => 8 };
        var placed = new List<(float X, float Z)>();
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (!ctx.TryRandomLandPoint(ref rng, 0.9f, 60f, out float x, out float z)) break;
                if (placed.Any(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < 140f * 140f)) continue;
                placed.Add((x, z));
                float top = rng.Range(bands.SkyStart + 12f, bands.SpaceStart - 16f);
                float radius = rng.Range(18f, 32f), depth = rng.Range(14f, 24f);
                var island = new IslandFeature(x, z, top, radius, depth) { Kind = "island", Zone = "sky", CloudLake = rng.Chance(0.3f), Seed = rng.NextUInt() };
                ctx.Plan.AddFeature(island);
                StructurePlacementPass.AddStructure(ctx.Plan, "sky_house", SkyHouse(ctx, new Int3((int)x, (int)top + 1, (int)z), ref rng));
                break;
            }
        }
    }

    private static Blueprint SkyHouse(PlanContext ctx, Int3 floor, ref Pcg32 rng)
    {
        var bp = new Blueprint { Flags = CellFlags.None };
        int w = rng.Range(9, 13);
        var box = new Aabb3i(new Int3(floor.X - w / 2, floor.Y - 1, floor.Z - w / 2), new Int3(floor.X + w / 2, floor.Y + 8, floor.Z + w / 2));
        bp.Room(box, "sky_brick");
        bp.Fill(new Aabb3i(new Int3(box.Min.X, floor.Y + 3, box.Min.Z + 2), new Int3(box.Min.X + 1, floor.Y + 5, box.Max.Z - 2)), "glass");
        bp.Fill(new Aabb3i(new Int3(box.Max.X - 1, floor.Y + 3, box.Min.Z + 2), new Int3(box.Max.X, floor.Y + 5, box.Max.Z - 2)), "glass");
        bp.Carve(new Aabb3i(new Int3(floor.X - 1, floor.Y, box.Min.Z), new Int3(floor.X + 1, floor.Y + 4, box.Min.Z + 1)));
        bp.Object(ctx.Content.TileObjects.Get("gold_chest"), new Int3(floor.X - 1, floor.Y, box.Max.Z - 4), 0, 0, "sky_chest");
        bp.Object(ctx.Content.TileObjects.Get("candle"), new Int3(box.Min.X + 2, floor.Y, box.Min.Z + 2), 0);
        return bp;
    }

    public override void Generate(ChunkGenContext ctx)
    {
        var p = ctx.Palette;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is IslandFeature island)
            {
                FeatureCarver.ForEachCell(ctx, island.Bounds, (x, y, z, i) =>
                {
                    float dx = x + 0.5f - island.X, dz = z + 0.5f - island.Z;
                    float r = MathF.Sqrt(dx * dx + dz * dz);
                    float warp = 1f + _shape!.Sample(x, z) * 0.22f;
                    float topY = island.TopY + _shape.Sample(x * 0.5f, z * 0.5f) * 2f;
                    float t = (topY - y) / island.Depth;
                    if (t >= 0f && t <= 1f && r < island.Radius * warp * (1f - t * t))
                    {
                        float fromTop = topY - y;
                        ctx.Blocks[i] = fromTop < 1f ? p.Turf : fromTop < 5f ? p.Loam : p.Stone;
                        if (island.CloudLake && r < island.Radius * 0.35f && fromTop < 4f)
                        {
                            ctx.Blocks[i] = fromTop < 3f ? p.Air : p.RainCloud;
                            if (fromTop < 3f) ctx.EnsureLiquids()[i] = LiquidCode.Full(LiquidType.Water);
                        }
                    }
                    else if (t > -1f && t < 0.35f && r < island.Radius * warp * 1.3f && r > island.Radius * warp * 0.85f * (1f - t * t)
                             && ctx.Blocks[i] == p.Air && _shape.Sample(x, y, z) > 0.05f)
                    {
                        ctx.Blocks[i] = p.Cloud;
                    }
                });
            }
            else if (f is StructureFeature s && s.Kind == "sky_house")
            {
                FeatureCarver.Stamp(ctx, s.Blueprint);
            }
        }
    }
}
