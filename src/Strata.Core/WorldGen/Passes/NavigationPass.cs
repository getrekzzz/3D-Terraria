using Strata.Core.Common;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 19 — navigation generation: flood-fills the chunk's passable space to record which faces are
/// connected through air (used for underground visibility culling and long-range path planning) and
/// counts walkable standing cells (used by the spawn director).
/// </summary>
public sealed class NavigationPass : GenPass
{
    private bool[]? _solid;

    public override string Name => "navigation";
    public override int Number => 19;

    public override void Plan(PlanContext ctx) =>
        _solid = ctx.Content.Blocks.All.Select(b => b.IsSolid).ToArray();

    public override void Generate(ChunkGenContext ctx)
    {
        const int n = ChunkMath.Size;
        var passable = new bool[ChunkMath.Volume];
        int air = 0, walkable = 0;
        for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    ushort b = ctx.Blocks[ChunkGenContext.Index(x + 1, y + 1, z + 1)];
                    bool open = !_solid![b];
                    passable[ChunkMath.Index(x, y, z)] = open;
                    if (!open) continue;
                    air++;
                    if (y + 4 < ChunkGenContext.Size && _solid[ctx.Blocks[ChunkGenContext.Index(x + 1, y, z + 1)]]
                        && !_solid[ctx.Blocks[ChunkGenContext.Index(x + 1, y + 2, z + 1)]]
                        && !_solid[ctx.Blocks[ChunkGenContext.Index(x + 1, y + 3, z + 1)]])
                        walkable++;
                }
        ctx.Nav = new ChunkNavSummary(FaceConnectivity(passable), walkable, air);
    }

    /// <summary>15-bit mask: for each pair of faces, whether an air region touches both.</summary>
    public static ushort FaceConnectivity(bool[] passable)
    {
        const int n = ChunkMath.Size;
        var visited = new bool[passable.Length];
        var queue = new int[passable.Length];
        ushort mask = 0;
        for (int start = 0; start < passable.Length; start++)
        {
            if (!passable[start] || visited[start]) continue;
            int faces = 0, head = 0, tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                int i = queue[head++];
                var c = ChunkMath.FromIndex(i);
                if (c.X == n - 1) faces |= 1 << Faces.PosX;
                if (c.X == 0) faces |= 1 << Faces.NegX;
                if (c.Y == n - 1) faces |= 1 << Faces.PosY;
                if (c.Y == 0) faces |= 1 << Faces.NegY;
                if (c.Z == n - 1) faces |= 1 << Faces.PosZ;
                if (c.Z == 0) faces |= 1 << Faces.NegZ;
                for (int f = 0; f < Faces.Count; f++)
                {
                    var nb = c + Faces.Offsets[f];
                    if ((uint)nb.X >= n || (uint)nb.Y >= n || (uint)nb.Z >= n) continue;
                    int j = ChunkMath.Index(nb);
                    if (!passable[j] || visited[j]) continue;
                    visited[j] = true;
                    queue[tail++] = j;
                }
            }
            for (int a = 0; a < Faces.Count; a++)
                for (int b = a + 1; b < Faces.Count; b++)
                    if ((faces & (1 << a)) != 0 && (faces & (1 << b)) != 0)
                        mask |= (ushort)(1 << ChunkNavSummary.PairBit(a, b));
        }
        return mask;
    }
}
