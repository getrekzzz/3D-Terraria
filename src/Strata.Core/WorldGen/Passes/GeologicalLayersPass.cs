using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 3 — geological layers: soil near the surface, stone in the underground layer, dense deepstone
/// in the caverns and ash approaching the underworld, with noisy (never flat) boundaries.
/// </summary>
public sealed class GeologicalLayersPass : GenPass
{
    private NoiseField? _boundary;

    public override string Name => "strata";
    public override int Number => 3;

    public override void Plan(PlanContext ctx) =>
        _boundary = NoiseField.Create(ctx.SeedsFor(this), "boundary", 60f, NoiseFractal.Fbm, 3);

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        var p = ctx.Palette;
        var bands = ctx.Plan.Bands;
        int size = ChunkGenContext.Size;
        for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                int col = ChunkGenContext.Column(lx, lz);
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                float h = ctx.SurfaceHeight[col];
                float soil = ctx.SoilDepth[col];
                float wobble = _boundary!.Sample(wx, wz);
                float cavernLine = bands.CavernTop + wobble * 10f;
                float ashLine = bands.UnderworldTop + 14f + wobble * 8f;
                for (int ly = 0; ly < size; ly++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (ctx.Blocks[i] != p.Stone) continue;
                    int wy = ctx.Origin.Y + ly;
                    float surfaceDepth = h - wy;
                    if (surfaceDepth < soil) ctx.Blocks[i] = p.Loam;
                    else if (wy < ashLine) ctx.Blocks[i] = p.Ash;
                    else if (wy < cavernLine) ctx.Blocks[i] = p.Deepstone;
                }
            }
    }
}
