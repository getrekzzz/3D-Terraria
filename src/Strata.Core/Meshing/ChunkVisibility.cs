using Strata.Core.Common;

namespace Strata.Core.Meshing;

/// <summary>
/// Cave culling (the approach block games use): each chunk records which pairs of its six faces are joined by open
/// (see-through) cells, and a flood fill from the camera's chunk only walks from chunk to chunk through faces that
/// see each other, never turning back towards the camera. Chunks it cannot reach cannot be seen from the camera,
/// such as rock and caves sealed off underfoot while you walk the surface.
/// </summary>
public static class ChunkVisibility
{
    /// <summary>Every face sees every other face (open air, or a chunk not meshed yet).</summary>
    public const ushort AllConnected = 0x7FFF;

    private static readonly int[,] PairBit = BuildPairBits();

    [ThreadStatic] private static bool[]? _visited;
    [ThreadStatic] private static int[]? _stack;

    private static int[,] BuildPairBits()
    {
        var bits = new int[6, 6];
        int n = 0;
        for (int a = 0; a < 6; a++)
            for (int b = a + 1; b < 6; b++)
            {
                bits[a, b] = n;
                bits[b, a] = n;
                n++;
            }
        return bits;
    }

    /// <summary>Whether a chunk with this mask lets sight pass in through face <paramref name="a"/> and out through <paramref name="b"/>.</summary>
    public static bool Connected(ushort mask, int a, int b) => a == b || (mask & (1 << PairBit[a, b])) != 0;

    /// <summary>Face connectivity of the neighbourhood's centre chunk (bits per face pair; see <see cref="Connected"/>).</summary>
    public static ushort Compute(Neighborhood nb, RenderTables tables)
    {
        const int s = ChunkMath.Size, m = Neighborhood.Margin;
        var visited = _visited ??= new bool[ChunkMath.Volume];
        var stack = _stack ??= new int[ChunkMath.Volume];
        Array.Clear(visited);
        int mask = 0;
        for (int start = 0; start < ChunkMath.Volume && mask != AllConnected; start++)
        {
            if (visited[start] || Opaque(nb, tables, start)) continue;
            int faces = 0, top = 0;
            stack[top++] = start;
            visited[start] = true;
            while (top > 0)
            {
                int i = stack[--top];
                int x = i & (s - 1), z = (i >> ChunkMath.Shift) & (s - 1), y = i >> (ChunkMath.Shift * 2);
                if (x == 0) faces |= 1 << Faces.NegX;
                if (x == s - 1) faces |= 1 << Faces.PosX;
                if (y == 0) faces |= 1 << Faces.NegY;
                if (y == s - 1) faces |= 1 << Faces.PosY;
                if (z == 0) faces |= 1 << Faces.NegZ;
                if (z == s - 1) faces |= 1 << Faces.PosZ;
                if (x > 0) Push(i - 1);
                if (x < s - 1) Push(i + 1);
                if (z > 0) Push(i - s);
                if (z < s - 1) Push(i + s);
                if (y > 0) Push(i - s * s);
                if (y < s - 1) Push(i + s * s);
            }
            for (int a = 0; a < 6; a++)
                for (int b = a + 1; b < 6; b++)
                    if ((faces & (1 << a)) != 0 && (faces & (1 << b)) != 0) mask |= 1 << PairBit[a, b];

            void Push(int j)
            {
                if (visited[j] || Opaque(nb, tables, j)) return;
                visited[j] = true;
                stack[top++] = j;
            }
        }
        return (ushort)mask;

        static bool Opaque(Neighborhood nb, RenderTables tables, int core)
        {
            int x = core & (ChunkMath.Size - 1), z = (core >> ChunkMath.Shift) & (ChunkMath.Size - 1), y = core >> (ChunkMath.Shift * 2);
            int i = Neighborhood.Index(x + m, y + m, z + m);
            return tables.IsOpaque(nb.Blocks[i]) && (!nb.HasShapes || nb.Shapes[i] == 0);
        }
    }

    /// <summary>
    /// Chunks the camera could see into: a flood fill from <paramref name="camera"/> through faces each chunk connects,
    /// moving only away from the camera, within <paramref name="radius"/> chunks (horizontally) and
    /// <paramref name="radiusY"/> vertically. Chunks with no known mask count as open.
    /// </summary>
    public static HashSet<Int3> Visible(Int3 camera, Func<Int3, ushort> maskOf, int radius, int radiusY)
    {
        var seen = new HashSet<Int3> { camera };
        var queue = new Queue<(Int3 Chunk, int Entry, int Dirs)>();
        queue.Enqueue((camera, -1, 0));
        while (queue.Count > 0)
        {
            var (c, entry, dirs) = queue.Dequeue();
            ushort mask = maskOf(c);
            for (int d = 0; d < 6; d++)
            {
                int back = d ^ 1; // Faces pairs each direction with its opposite (PosX 0 / NegX 1, …)
                if ((dirs & (1 << back)) != 0) continue; // never turn back towards the camera
                if (entry >= 0 && !Connected(mask, entry, d)) continue;
                var n = c + Faces.Offsets[d];
                if (Math.Abs(n.X - camera.X) > radius || Math.Abs(n.Z - camera.Z) > radius || Math.Abs(n.Y - camera.Y) > radiusY) continue;
                if (!seen.Add(n)) continue;
                queue.Enqueue((n, back, dirs | (1 << d)));
            }
        }
        return seen;
    }
}
