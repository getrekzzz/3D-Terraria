using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.World;

namespace Strata.Core.WorldGen.Analysis;

/// <summary>
/// Cave census of a window of generated world, from the underworld's top up to the surface: how much of the ground is
/// open, how that open space hangs together (connected pieces, what the sky reaches), how much of it a player can stand
/// in, and which chests wait there. The before/after evidence for cave changes (worldgen-report). The metric
/// definitions (constants below) stay fixed so older reports remain comparable.
/// </summary>
public static partial class CaveProfile
{
    /// <summary>Open pieces smaller than this many cells are pockets: bubbles too small to explore.</summary>
    public const int PocketCells = 512;

    /// <summary>Open cells a player needs over a floor (the player is 3.6 cells tall).</summary>
    public const int Headroom = 4;

    /// <summary>Open cells over a floor that make a hall rather than a passage.</summary>
    public const int HallHeadroom = 16;

    /// <summary>Cells under the column's surface height where cave counting starts (skips the surface's own dips).</summary>
    public const int SurfaceMargin = 3;

    private const byte Solid = 0, Air = 1, Liquid = 2;

    /// <summary>
    /// Generates every chunk of the window from the underworld's top to above its highest ground and measures it.
    /// Ordinary worlds only (a Don't Dig Up world's caves lie above its surface).
    /// </summary>
    public static CaveStats Measure(WorldGenerator gen, int x0, int z0, int width, int depth, int threads = 1)
    {
        var size = gen.Plan.Size;
        var bands = gen.Plan.Bands;
        if (bands.Inverted) throw new NotSupportedException("cave census needs an ordinary world");
        int xa = Math.Clamp(x0, 0, size.X), za = Math.Clamp(z0, 0, size.Z);
        int xb = Math.Clamp(x0 + width, 0, size.X), zb = Math.Clamp(z0 + depth, 0, size.Z);
        int w = xb - xa, d = zb - za;
        if (w <= 0 || d <= 0) throw new ArgumentException("window lies outside the world");

        var surface = new float[w * d];
        float highest = float.MinValue;
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                float s = gen.Terrain.SurfaceHeight(xa + x, za + z);
                surface[z * w + x] = s;
                highest = MathF.Max(highest, s);
            }
        int y0 = bands.UnderworldTop, y1 = Math.Min(size.Y - 1, (int)highest + 24);
        int h = y1 - y0 + 1;
        var grid = new byte[(long)w * d * h > int.MaxValue ? throw new ArgumentException("window too large") : w * d * h];
        var solid = gen.Content.Blocks.All.Select(b => b.IsSolid).ToArray();
        var objects = new List<(string Id, WorldLayer Layer)>();
        int cx0 = ChunkMath.ToChunk(xa), cz0 = ChunkMath.ToChunk(za), ncx = ChunkMath.ToChunk(xb - 1) - cx0 + 1, ncz = ChunkMath.ToChunk(zb - 1) - cz0 + 1;
        int cy0 = ChunkMath.ToChunk(y0), cy1 = ChunkMath.ToChunk(y1);

        void Column(int k)
        {
            int cx = cx0 + k % ncx, cz = cz0 + k / ncx;
            for (int cy = cy0; cy <= cy1; cy++)
            {
                var chunk = gen.Generate(new Int3(cx, cy, cz));
                var origin = ChunkMath.ChunkOrigin(chunk.Coord);
                for (int ly = 0; ly < ChunkMath.Size; ly++)
                {
                    int gy = origin.Y + ly - y0;
                    if ((uint)gy >= (uint)h) continue;
                    for (int lz = 0; lz < ChunkMath.Size; lz++)
                    {
                        int gz = origin.Z + lz - za;
                        if ((uint)gz >= (uint)d) continue;
                        for (int lx = 0; lx < ChunkMath.Size; lx++)
                        {
                            int gx = origin.X + lx - xa;
                            if ((uint)gx >= (uint)w) continue;
                            int i = ChunkMath.Index(lx, ly, lz);
                            byte state = solid[chunk.Data.GetBlock(i)] ? Solid : (chunk.Data.Liquids?[i] ?? 0) != 0 ? Liquid : Air;
                            grid[(gy * d + gz) * w + gx] = state;
                        }
                    }
                }
                lock (objects)
                    foreach (var o in chunk.Objects)
                        if (o.Anchor.X >= xa && o.Anchor.X < xb && o.Anchor.Z >= za && o.Anchor.Z < zb && o.Anchor.Y >= y0 && o.Anchor.Y <= y1)
                            objects.Add((o.Def.Id, bands.LayerAt(o.Anchor.Y)));
            }
        }

        int count = ncx * ncz;
        if (threads > 1) Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = threads }, Column);
        else for (int k = 0; k < count; k++) Column(k);
        return Analyse(gen, grid, surface, w, d, h, y0, objects, count * (cy1 - cy0 + 1));
    }

    private static CaveStats Analyse(WorldGenerator gen, byte[] grid, float[] surface, int w, int d, int h, int y0,
        List<(string Id, WorldLayer Layer)> objects, int chunks)
    {
        var bands = gen.Plan.Bands;
        bool IsCave(int x, int y, int z) => y + y0 < surface[z * w + x] - SurfaceMargin;
        int layer = w * d;

        // Connected open pieces (6-neighbour flood fill); the sky's piece is whatever the top row reaches.
        var label = new int[grid.Length];
        var sizes = new List<long> { 0 };
        var skyLinked = new List<bool> { false };
        var queue = new int[Math.Min(grid.Length, 1 << 26)];
        for (int start = 0; start < grid.Length; start++)
        {
            if (grid[start] == Solid || label[start] != 0) continue;
            int id = sizes.Count;
            long caveCells = 0;
            bool sky = false;
            int head = 0, tail = 0;
            label[start] = id;
            queue[tail++] = start;
            while (head != tail)
            {
                int i = queue[head++];
                if (head == queue.Length) head = 0;
                int y = i / layer, rem = i - y * layer, z = rem / w, x = rem - z * w;
                if (IsCave(x, y, z)) caveCells++;
                else if (y == h - 1) sky = true;
                Visit(x > 0 ? i - 1 : -1);
                Visit(x < w - 1 ? i + 1 : -1);
                Visit(z > 0 ? i - w : -1);
                Visit(z < d - 1 ? i + w : -1);
                Visit(y > 0 ? i - layer : -1);
                Visit(y < h - 1 ? i + layer : -1);
            }
            sizes.Add(caveCells);
            skyLinked.Add(sky);

            void Visit(int j)
            {
                if (j < 0 || grid[j] == Solid || label[j] != 0) return;
                label[j] = id;
                queue[tail++] = j;
                if (tail == queue.Length) tail = 0;
            }
        }

        var perLayer = new Dictionary<WorldLayer, (long Cells, long Open)>();
        long cave = 0, floor = 0, hall = 0, cramped = 0, pocketCells = 0, skyCells = 0;
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                int run = 0; // open cells above the current one in this column (counted downwards)
                for (int y = h - 1; y >= 0; y--)
                {
                    int i = (y * d + z) * w + x;
                    byte s = grid[i];
                    if (!IsCave(x, y, z))
                    {
                        run = s == Solid ? 0 : run + 1;
                        continue;
                    }
                    var l = bands.LayerAt(y + y0);
                    perLayer.TryGetValue(l, out var acc);
                    acc.Cells++;
                    if (s != Solid)
                    {
                        acc.Open++;
                        cave++;
                        long size = sizes[label[i]];
                        if (size < PocketCells) pocketCells++;
                        if (skyLinked[label[i]]) skyCells++;
                        if (s == Air && y > 0 && grid[i - layer] == Solid)
                        {
                            if (run + 1 >= Headroom) floor++;
                            if (run + 1 >= HallHeadroom) hall++;
                        }
                    }
                    perLayer[l] = acc;
                    run = s == Solid ? 0 : run + 1;
                }
                // Cramped: open runs shorter than a player, measured as whole runs.
                int len = 0;
                for (int y = 0; y <= h; y++)
                {
                    bool open = y < h && grid[(y * d + z) * w + x] != Solid && IsCave(x, y, z);
                    if (open) { len++; continue; }
                    if (len > 0 && len < Headroom) cramped += len;
                    len = 0;
                }
            }

        long largest = 0;
        int pieces = 0, pockets = 0;
        for (int id = 1; id < sizes.Count; id++)
        {
            if (sizes[id] == 0) continue;
            if (sizes[id] >= PocketCells) pieces++;
            else pockets++;
            if (!skyLinked[id]) largest = Math.Max(largest, sizes[id]);
        }
        largest = Math.Max(largest, skyCells);

        var (caveFloor, reached, roundTrip) = WalkReach(grid, surface, w, d, h, y0);
        double Pct(long part, long whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 2);
        return new CaveStats
        {
            Chunks = chunks,
            CaveCells = cave,
            OpenPercent = perLayer.Where(kv => kv.Key is WorldLayer.Underground or WorldLayer.Cavern)
                .ToDictionary(kv => kv.Key.ToString(), kv => Pct(kv.Value.Open, kv.Value.Cells)),
            SkyLinkedPercent = Pct(skyCells, cave),
            LargestPiecePercent = Pct(largest, cave),
            Pieces = pieces,
            PocketsPerMillion = Math.Round(pockets * 1e6 / Math.Max(1, cave), 1),
            PocketPercent = Pct(pocketCells, cave),
            FloorPercent = Pct(floor, cave),
            HallFloorPercent = Pct(hall, Math.Max(1, floor)),
            CrampedPercent = Pct(cramped, cave),
            WalkInPercent = Pct(reached, caveFloor),
            RoundTripPercent = Pct(roundTrip, caveFloor),
            Objects = objects.Where(o => o.Id.EndsWith("_chest", StringComparison.Ordinal) || o.Id == "pot")
                .GroupBy(o => $"{o.Layer}/{o.Id}").OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
        };
    }
}

/// <summary>Results of a <see cref="CaveProfile"/> census (percentages are of cave cells unless named otherwise).</summary>
public sealed class CaveStats
{
    public int Chunks { get; init; }
    public long CaveCells { get; init; }

    /// <summary>Open (air or liquid) share of the ground, per layer.</summary>
    public Dictionary<string, double> OpenPercent { get; init; } = new();

    /// <summary>Cave cells connected to the open sky.</summary>
    public double SkyLinkedPercent { get; init; }

    /// <summary>Cave cells in the largest connected piece (the sky's, when that is largest).</summary>
    public double LargestPiecePercent { get; init; }

    /// <summary>Connected pieces of at least <see cref="CaveProfile.PocketCells"/> cave cells.</summary>
    public int Pieces { get; init; }

    /// <summary>Pieces smaller than <see cref="CaveProfile.PocketCells"/>, per million cave cells.</summary>
    public double PocketsPerMillion { get; init; }

    public double PocketPercent { get; init; }

    /// <summary>Cave cells a player can stand on (solid below, <see cref="CaveProfile.Headroom"/> open above).</summary>
    public double FloorPercent { get; init; }

    /// <summary>Share of that floor under <see cref="CaveProfile.HallHeadroom"/> or more open cells.</summary>
    public double HallFloorPercent { get; init; }

    /// <summary>Cave cells in vertical gaps lower than a player.</summary>
    public double CrampedPercent { get; init; }

    /// <summary>Cave floor (standable cells) a player reaches on foot from the surface: walking, stepping, jumping up to
    /// <see cref="CaveProfile.JumpCells"/> and falling at most <see cref="CaveProfile.SafeFallCells"/>, never digging.</summary>
    public double WalkInPercent { get; init; }

    /// <summary>Cave floor a player reaches on foot from the surface and can also walk back out from.</summary>
    public double RoundTripPercent { get; init; }

    /// <summary>Chests and pots by layer.</summary>
    public Dictionary<string, int> Objects { get; init; } = new();
}
