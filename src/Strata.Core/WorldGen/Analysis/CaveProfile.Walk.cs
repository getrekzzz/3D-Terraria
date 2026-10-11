namespace Strata.Core.WorldGen.Analysis;

public static partial class CaveProfile
{
    /// <summary>Highest ledge a player climbs with a jump (the apex is about 5.5 cells).</summary>
    public const int JumpCells = 5;

    /// <summary>Longest drop without fall damage (cells).</summary>
    public const int SafeFallCells = 25;

    /// <summary>
    /// Explorability on foot: a graph over standable cells (solid below, <see cref="Headroom"/> open above) whose moves go
    /// to the four neighbouring columns: up at most <see cref="JumpCells"/> with the head clear over the start, down at
    /// most <see cref="SafeFallCells"/> with the drop clear. Walks in from every standable cell on the surface, then
    /// back out along reversed moves. Returns the cave floor (standable cells under the surface), how much of it a
    /// player reaches, and how much of that they can also leave again.
    /// </summary>
    private static (long Floor, long Reached, long RoundTrip) WalkReach(byte[] grid, float[] surface, int w, int d, int h, int y0)
    {
        int layer = w * d;
        bool Open(int x, int y, int z) => y >= 0 && y < h && grid[(y * d + z) * w + x] != Solid;

        // Standable cells per column, as (column, y) packed into the index of the grid.
        var standable = new List<int>[layer];
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                var list = new List<int>();
                int run = 0;
                for (int y = h - 1; y >= 1; y--)
                {
                    int i = (y * d + z) * w + x;
                    run = grid[i] == Solid ? 0 : run + 1;
                    if (grid[i] == Air && grid[i - layer] == Solid && run >= Headroom) list.Add(y);
                }
                standable[z * w + x] = list;
            }

        bool Clear(int x, int z, int from, int to)
        {
            for (int y = from; y <= to; y++)
                if (!Open(x, y, z)) return false;
            return true;
        }

        // Whether a player standing at (px, py, pz) gets to the standable cell (cx, cy, cz) in a neighbouring column.
        bool Move(int px, int py, int pz, int cx, int cy, int cz)
        {
            int dy = cy - py;
            if (dy >= 0) return dy <= JumpCells && Clear(px, pz, py, py + dy + Headroom - 1);
            return -dy <= SafeFallCells && Clear(cx, cz, cy, py + Headroom - 1);
        }

        long floor = 0;
        var forward = new HashSet<long>();
        var queue = new Queue<(int X, int Y, int Z)>();
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
                foreach (int y in standable[z * w + x])
                {
                    if (y + y0 < surface[z * w + x] - SurfaceMargin) floor++;
                    else if (forward.Add(Key(x, y, z))) queue.Enqueue((x, y, z));
                }
        var starts = forward.ToList();
        Flood(forward, queue, reverse: false);

        var back = new HashSet<long>(starts);
        foreach (long k in starts) queue.Enqueue(Unpack(k));
        Flood(back, queue, reverse: true);

        long reached = 0, both = 0;
        foreach (long k in forward)
        {
            var (x, y, z) = Unpack(k);
            if (y + y0 >= surface[z * w + x] - SurfaceMargin) continue;
            reached++;
            if (back.Contains(k)) both++;
        }
        return (floor, reached, both);

        void Flood(HashSet<long> seen, Queue<(int X, int Y, int Z)> q, bool reverse)
        {
            while (q.Count > 0)
            {
                var (x, y, z) = q.Dequeue();
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + (n == 0 ? 1 : n == 1 ? -1 : 0), nz = z + (n == 2 ? 1 : n == 3 ? -1 : 0);
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)d) continue;
                    foreach (int ny in standable[nz * w + nx])
                    {
                        if (ny > y + SafeFallCells || ny < y - SafeFallCells) continue;
                        bool ok = reverse ? Move(nx, ny, nz, x, y, z) : Move(x, y, z, nx, ny, nz);
                        if (ok && seen.Add(Key(nx, ny, nz))) q.Enqueue((nx, ny, nz));
                    }
                }
            }
        }

        long Key(int x, int y, int z) => ((long)y * d + z) * w + x;

        (int X, int Y, int Z) Unpack(long k)
        {
            int y = (int)(k / layer), rem = (int)(k - (long)y * layer);
            return (rem % w, y, rem / w);
        }
    }
}
