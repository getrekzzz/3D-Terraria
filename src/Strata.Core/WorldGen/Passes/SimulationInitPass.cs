using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 20 — simulation initialization: reconciles liquids with the final geometry (no liquid inside
/// solids; water meeting lava hardens into obsidian) so the liquid simulation starts from a stable state.
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
