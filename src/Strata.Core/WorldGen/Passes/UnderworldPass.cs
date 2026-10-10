using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Structures;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 13 — the Cinderdeep: a vast volcanic cavity spanning the world's base, with an ash ceiling,
/// rolling ash hills, a glowing lava sea and ruined obsidian towers. In a Don't Dig Up world it spans the world's top
/// instead (the same floor, lava sea and ruins, under a thin ash crust) and holds the player's spawn.
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
        if (ctx.Plan.Bands.Inverted) ctx.Plan.Spawn = InvertedSpawn(ctx);
    }

    /// <summary>
    /// Don't Dig Up: the player starts in the underworld near the top — on dry ash floor near the world's centre, with no
    /// lava sea within a dozen cells, open headroom and no ruined tower in the way (spiralling out from the centre).
    /// </summary>
    private Int3 InvertedSpawn(PlanContext ctx)
    {
        var bands = ctx.Plan.Bands;
        int cx = (int)ctx.CenterX, cz = (int)ctx.CenterZ;
        var towers = ctx.Plan.Features.OfKind<StructureFeature>("ruin_tower").Select(t => t.Bounds).ToList();
        for (int r = 0; r <= 640; r += 8)
            for (int dz = -r; dz <= r; dz += 8)
                for (int dx = -r; dx <= r; dx += 8)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (SafeSpawn(bands, x, z, towers, out int y)) return new Int3(x, y, z);
                }
        return new Int3(cx, (int)FloorHeight(bands, cx, cz) + 1, cz);
    }

    private bool SafeSpawn(Plan.LayerBands bands, int x, int z, List<Aabb3i> towers, out int y)
    {
        float ground = FloorHeight(bands, x, z);
        y = (int)MathF.Floor(ground) + 1;
        if (ground < bands.LavaLevel + 4f || CeilingHeight(bands, x, z) - ground < 14f) return false;
        for (int oz = -12; oz <= 12; oz += 3)
            for (int ox = -12; ox <= 12; ox += 3)
            {
                float f = FloorHeight(bands, x + ox, z + oz);
                if (f < bands.LavaLevel + 3f || MathF.Abs(f - ground) > (MathF.Abs(ox) + MathF.Abs(oz) <= 6 ? 2f : 6f)) return false;
            }
        var near = new Aabb3i(new Int3(x - 16, y - 4, z - 16), new Int3(x + 17, y + 12, z + 17));
        foreach (var t in towers)
            if (t.Intersects(near)) return false;
        return true;
    }

    private float CeilingHeight(Plan.LayerBands bands, float x, float z) => bands.Inverted
        ? bands.UnderworldCeiling + 10f + _ceiling!.Sample(x, z) * 6f
        : bands.UnderworldTop - 5f + _ceiling!.Sample(x, z) * 6f;

    private float FloorHeight(Plan.LayerBands bands, float x, float z)
    {
        float lake = DetMath.SmoothStep(-0.05f, 0.25f, _lakes!.Sample(x, z));
        return bands.LavaLevel + 2f + _floor!.Sample01(x, z) * bands.UnderworldSpan * 0.32f * (1f - lake) - lake * 6f;
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
        bp.Object(ctx.Content.TileObjects.Get("shadow_chest"), new Int3(ground.X - 1, ground.Y - 1, ground.Z + 1), 0, 0, "shadow_chest", locked: true);
        return bp;
    }

    public override void Generate(ChunkGenContext ctx)
    {
        var bands = ctx.Plan.Bands;
        int n = ChunkGenContext.Size;
        bool inverted = bands.Inverted;
        if (inverted ? ctx.Origin.Y + n < bands.UnderworldTop - 2 : ctx.Origin.Y > bands.UnderworldTop + 2) return;
        var p = ctx.Palette;
        int top = ctx.Plan.Size.Y - 3;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                float floor = FloorHeight(bands, wx, wz);
                float ceiling = CeilingHeight(bands, wx, wz);
                for (int ly = 0; ly < n; ly++)
                {
                    int wy = ctx.Origin.Y + ly;
                    if (inverted ? wy < bands.UnderworldTop || wy >= top : wy < 3 || wy >= bands.UnderworldTop) continue;
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (wy > floor && wy < ceiling)
                    {
                        ctx.Blocks[i] = p.Air;
                        if (wy <= bands.LavaLevel) ctx.EnsureLiquids()[i] = LiquidCode.Full(LiquidType.Lava);
                    }
                    else if (ctx.Blocks[i] != p.Air && ctx.Blocks[i] != p.Cinderstone && ctx.Blocks[i] != p.Bedrock)
                    {
                        // Don't Dig Up: all ash, the floor mass below and the thin crust above the cavity.
                        ctx.Blocks[i] = inverted || wy < floor ? p.Ash : ctx.Blocks[i] == p.Deepstone || wy > ceiling + 6 ? ctx.Blocks[i] : p.Ash;
                    }
                }
            }
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
            if (f is StructureFeature s && s.Kind == "ruin_tower") FeatureCarver.Stamp(ctx, s.Blueprint);
    }
}
