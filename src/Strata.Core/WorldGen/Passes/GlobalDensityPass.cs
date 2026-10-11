using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 1 — the global terrain density field. Fills natural solid (as stone, refined later) wherever the
/// surface-height density with near-surface 3D overhang noise is positive (a Don't Dig Up world adds the rock roof over
/// its bottom surface, solid up to bedrock at the world's top).
/// </summary>
public sealed class GlobalDensityPass : GenPass
{
    public override string Name => "density";
    public override int Number => 1;

    public override void Generate(ChunkGenContext ctx)
    {
        var p = ctx.Palette;
        var terrain = ctx.Gen.Terrain;
        int size = ChunkGenContext.Size;
        bool anySolid = false;
        var roof = ctx.Ceiling; // Don't Dig Up: the rock roof over the bottom surface, bedrock at the world's top
        int top = ctx.Plan.Size.Y - 3;
        for (int lz = 0; lz < size; lz++)
            for (int lx = 0; lx < size; lx++)
            {
                int col = ChunkGenContext.Column(lx, lz);
                float h = ctx.SurfaceHeight[col];
                float amp = ctx.OverhangAmp[col];
                float band = amp + 1f;
                float cliff = ctx.Cliff[col];
                int wx = ctx.Origin.X + lx, wz = ctx.Origin.Z + lz;
                for (int ly = 0; ly < size; ly++)
                {
                    int wy = ctx.Origin.Y + ly;
                    ushort block;
                    if (wy < 3) block = p.Bedrock;
                    else if (wy < h - band) block = p.Stone;
                    else if (roof != null && wy >= top) block = p.Bedrock;
                    else if (roof != null && wy >= roof[col]) block = p.Stone;
                    else if (wy > h + band) block = p.Air;
                    else block = terrain.Density(wx, wy, wz, h, amp, cliff) > 0f ? p.Stone : p.Air;
                    ctx.Blocks[ChunkGenContext.Index(lx, ly, lz)] = block;
                    anySolid |= block != p.Air;
                }
            }
        ctx.AllAir = !anySolid;
    }
}
