using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 15 — the ocean: sandy seabed and open water filling the basin around the continent up to sea
/// level (never flooding cave systems beneath the shore — liquids simulation takes over from here).
/// </summary>
public sealed class OceanPass : GenPass
{
    public override string Name => "ocean";
    public override int Number => 15;

    public override void Generate(ChunkGenContext ctx)
    {
        int sea = ctx.Plan.Bands.SeaLevel;
        if (ctx.Origin.Y > sea) return;
        var p = ctx.Palette;
        byte water = LiquidCode.Full(LiquidType.Water);
        int n = ChunkGenContext.Size;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
            {
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                if (ctx.Plan.Landness.Sample(wx, wz) > 0.62f) continue;
                float h = ctx.SurfaceHeight[ChunkGenContext.Column(lx, lz)];
                for (int ly = 0; ly < n; ly++)
                {
                    int wy = ctx.Origin.Y + ly;
                    if (wy > sea || wy < h - 3f) continue;
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (ctx.Blocks[i] == p.Air) ctx.EnsureLiquids()[i] = water;
                    else if (ctx.Blocks[i] == p.Loam || ctx.Blocks[i] == p.Turf) ctx.Blocks[i] = p.Sand;
                }
            }
    }
}
