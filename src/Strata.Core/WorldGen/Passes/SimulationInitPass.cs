using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 20 — simulation initialization: reconciles liquids with the final geometry (no liquid inside
/// solids; water meeting lava hardens into obsidian; no liquid left hanging over space that later passes carved
/// out beneath it) so generated liquid starts out settled.
/// </summary>
public sealed class SimulationInitPass : GenPass
{
    private bool[]? _solid;

    public override string Name => "simulation";
    public override int Number => 20;

    public override void Plan(PlanContext ctx) =>
        _solid = ctx.Content.Blocks.All.Select(b => b.IsSolid).ToArray();

    public override void Generate(ChunkGenContext ctx)
    {
        var liquids = ctx.Liquids;
        if (liquids == null) return;
        int n = ChunkGenContext.Size;
        for (int ly = 0; ly < n; ly++)
            for (int lz = 0; lz < n; lz++)
                for (int lx = 0; lx < n; lx++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    byte liquid = liquids[i];
                    if (liquid == 0) continue;
                    if (_solid![ctx.Blocks[i]])
                    {
                        liquids[i] = 0;
                        continue;
                    }
                    if (LiquidCode.Type(liquid) != LiquidType.Lava) continue;
                    if (TouchesWater(liquids, lx, ly, lz))
                    {
                        liquids[i] = 0;
                        ctx.Blocks[i] = ctx.Palette.Obsidian;
                    }
                }
        DropHangingLiquid(ctx, liquids);
    }

    /// <summary>
    /// Liquid resting on open, dry space (a river channel crossing a cave carved by another pass) would pour down
    /// the moment the chunk loads. Bottom-up per column, so a whole hanging column goes. The chunk's lowest layer
    /// cannot see below the chunk and is left alone.
    /// </summary>
    private void DropHangingLiquid(ChunkGenContext ctx, byte[] liquids)
    {
        int n = ChunkGenContext.Size;
        for (int lz = 0; lz < n; lz++)
            for (int lx = 0; lx < n; lx++)
                for (int ly = 1; ly < n; ly++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    if (liquids[i] == 0) continue;
                    int below = ChunkGenContext.Index(lx, ly - 1, lz);
                    if (!_solid![ctx.Blocks[below]] && liquids[below] == 0) liquids[i] = 0;
                }
    }

    private static bool TouchesWater(byte[] liquids, int lx, int ly, int lz)
    {
        int n = ChunkGenContext.Size;
        foreach (var o in Common.Faces.Offsets)
        {
            int x = lx + o.X, y = ly + o.Y, z = lz + o.Z;
            if ((uint)x >= n || (uint)y >= n || (uint)z >= n) continue;
            if (LiquidCode.Type(liquids[ChunkGenContext.Index(x, y, z)]) == LiquidType.Water) return true;
        }
        return false;
    }
}
