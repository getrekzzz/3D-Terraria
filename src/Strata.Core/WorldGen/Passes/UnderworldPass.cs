using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 13 — the Cinderdeep: a vast volcanic cavity spanning the world's base, with an ash ceiling,
/// rolling ash hills, a glowing lava sea and ruined obsidian towers.
/// </summary>
public sealed class UnderworldPass : GenPass
{
    private NoiseField? _floor, _ceiling, _lakes;

    public override string Name => "underworld";
    public override int Number => 13;

    public override void Plan(PlanContext ctx)
    {
        var seeds = ctx.SeedsFor(this);
        _floor = NoiseField.Create(seeds, "floor", 90f, NoiseFractal.Ridged, 3);
        _ceiling = NoiseField.Create(seeds, "ceiling", 60f, NoiseFractal.Fbm, 3);
        _lakes = NoiseField.Create(seeds, "lakes", 260f, NoiseFractal.Fbm, 2);

        var rng = seeds.Rng("ruins");
        for (int i = 0, n = Math.Max(2, ctx.Scaled(10)); i < n; i++)
        {
            float x = rng.Range(ctx.Plan.Size.X * 0.1f, ctx.Plan.Size.X * 0.9f), z = rng.Range(ctx.Plan.Size.Z * 0.1f, ctx.Plan.Size.Z * 0.9f);
            int ground = (int)FloorHeight(ctx.Plan.Bands, x, z);
            if (ground <= ctx.Plan.Bands.LavaLevel + 2) continue;
            StructurePlacementPass.AddStructure(ctx.Plan, "ruin_tower", Tower(ctx, new Int3((int)x, ground, (int)z), ref rng));
        }
    }

    private float FloorHeight(Plan.LayerBands bands, float x, float z)
    {
        float lake = DetMath.SmoothStep(-0.05f, 0.25f, _lakes!.Sample(x, z));
        return bands.LavaLevel + 2f + _floor!.Sample01(x, z) * bands.UnderworldTop * 0.32f * (1f - lake) - lake * 6f;
    }

    private static Blueprint Tower(PlanContext ctx, Int3 ground, ref Pcg32 rng)
    {
        var bp = new Blueprint();
        int w = rng.Range(8, 13), h = rng.Range(14, 26);
        var box = new Aabb3i(new Int3(ground.X - w / 2, ground.Y - 2, ground.Z - w / 2), new Int3(ground.X + w / 2, ground.Y + h, ground.Z + w / 2));
        bp.Room(box, "obsidian_brick");
        for (int y = box.Min.Y + 7; y < box.Max.Y - 2; y += 7)
            bp.Fill(new Aabb3i(new Int3(box.Min.X + 1, y, box.Min.Z + 1), new Int3(box.Max.X - 1, y + 1, box.Max.Z - 1)), "obsidian_brick", ShapeCode.Pack(CellShape.Platform, 0));
        bp.Carve(new Aabb3i(new Int3(ground.X - 1, ground.Y - 1, box.Min.Z), new Int3(ground.X + 2, ground.Y + 4, box.Min.Z + 1)));
        if (rng.Chance(0.5f)) bp.Carve(new Aabb3i(new Int3(box.Min.X, box.Max.Y - 6, box.Min.Z + 2), new Int3(box.Min.X + 3, box.Max.Y, box.Max.Z - 2)));
        bp.Object(ctx.Content.TileObjects.Get("gold_chest"), new Int3(ground.X - 1, ground.Y - 1, ground.Z + 1), 0, 0, "cabin_chest");
        return bp;
    }

    public override void Generate(ChunkGenContext ctx)
    {
        var bands = ctx.Plan.Bands;
        if (ctx.Origin.Y > bands.UnderworldTop + 2) return;
        var p = ctx.Palette;
        int n = ChunkGenContext.Size;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                float floor = FloorHeight(bands, wx, wz);
                float ceiling = bands.UnderworldTop - 5f + _ceiling!.Sample(wx, wz) * 6f;
                for (int ly = 0; ly < n; ly++)
                {
                    int wy = ctx.Origin.Y + ly;
                    if (wy < 3 || wy >= bands.UnderworldTop) continue;
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (wy > floor && wy < ceiling)
                    {
                        ctx.Blocks[i] = p.Air;
                        if (wy <= bands.LavaLevel) ctx.EnsureLiquids()[i] = LiquidCode.Full(LiquidType.Lava);
                    }
                    else if (ctx.Blocks[i] != p.Air && ctx.Blocks[i] != p.Cinderstone && ctx.Blocks[i] != p.Bedrock)
                    {
                        ctx.Blocks[i] = wy < floor ? p.Ash : ctx.Blocks[i] == p.Deepstone || wy > ceiling + 6 ? ctx.Blocks[i] : p.Ash;
                    }
                }
            }
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is StructureFeature s && s.Kind == "ruin_tower") FeatureCarver.Stamp(ctx, s.Blueprint);
    }
}
