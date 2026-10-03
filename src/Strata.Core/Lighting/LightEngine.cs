using Strata.Core.Common;
using Strata.Core.Meshing;
using Strata.Core.Voxels;

namespace Strata.Core.Lighting;

/// <summary>A light source not stored in voxels (placed tile objects: torches, candles, furnaces).</summary>
public readonly record struct LightSource(Int3 Cell, ushort Light);

/// <summary>
/// Stateless flood-fill lighting over a <see cref="Neighborhood"/>: sunlight seeded from the sky map and
/// spread sideways/down with attenuation, plus coloured block light from emitters (blocks, lava, objects).
/// Exact for the centre chunk because light never travels more than 15 cells.
/// </summary>
public static class LightEngine
{
    private const int N = Neighborhood.Size;

    [ThreadStatic] private static int[]? _queue;

    /// <summary>Sky light level of outdoor shade under a floating roof (see <see cref="SkyMap.ShadeTop"/>).</summary>
    public const int ShadeLight = 12;

    public static ushort[] Compute(Neighborhood nb, RenderTables tables, Func<int, int, int> skyTop, IReadOnlyList<LightSource> sources,
        ushort[]? into = null, Func<int, int, int>? shadeTop = null, Func<int, int, int>? shadeCap = null)
    {
        var light = into ?? new ushort[Neighborhood.Volume];
        Array.Clear(light);
        var queue = _queue ??= new int[Neighborhood.Volume * 2];
        var filter = tables.LightFilter;
        var blocks = nb.Blocks;
        var liquids = nb.Liquids;

        // --- Sky: fill exposed column tops, then seed the frontier.
        var tops = new int[N * N];
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int top = Math.Clamp(skyTop(nb.Origin.X + x, nb.Origin.Z + z) - nb.Origin.Y, 0, N);
                tops[z * N + x] = top;
                for (int y = top; y < N; y++) light[Neighborhood.Index(x, y, z)] = LightValue.Pack(15, 0, 0, 0);
            }

        int head = 0, tail = 0;
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int top = tops[z * N + x];
                if (top >= N) continue;
                int highestNeighbour = top;
                if (x > 0) highestNeighbour = Math.Max(highestNeighbour, tops[z * N + x - 1]);
                if (x < N - 1) highestNeighbour = Math.Max(highestNeighbour, tops[z * N + x + 1]);
                if (z > 0) highestNeighbour = Math.Max(highestNeighbour, tops[(z - 1) * N + x]);
                if (z < N - 1) highestNeighbour = Math.Max(highestNeighbour, tops[(z + 1) * N + x]);
                for (int y = top; y < Math.Min(N, highestNeighbour + 1); y++) queue[tail++] = Neighborhood.Index(x, y, z);
            }
        // Open air under a floating roof (a sky island, an arch): outdoor shade, spreading like sunlight.
        if (shadeTop != null)
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int top = tops[z * N + x];
                    int shade = Math.Clamp(shadeTop(nb.Origin.X + x, nb.Origin.Z + z) - nb.Origin.Y, 0, N);
                    // Under a Don't Dig Up world's rock roof the shade ends at the roof (the caves above stay dark).
                    int end = shadeCap == null ? top : Math.Min(top, (int)Math.Clamp((long)shadeCap(nb.Origin.X + x, nb.Origin.Z + z) - nb.Origin.Y, 0, N));
                    for (int y = shade; y < end; y++)
                    {
                        int i = Neighborhood.Index(x, y, z);
                        if (filter[blocks[i]] != 0 || liquids[i] != 0 || LightValue.Sky(light[i]) >= ShadeLight) continue;
                        light[i] = LightValue.Pack(ShadeLight, 0, 0, 0);
                        queue[tail++] = i;
                    }
                }
        Propagate(light, queue, ref head, ref tail, nb, filter, sky: true);

        // --- Block light from emitters.
        head = tail = 0;
        var emission = tables.Emission;
        for (int i = 0; i < Neighborhood.Volume; i++)
        {
            ushort e = emission[blocks[i]];
            if (liquids[i] != 0 && tables.LiquidEmission[liquids[i] >> 5] is var le and not 0) e = le;
            if (e == 0) continue;
            light[i] = Merge(light[i], e);
            queue[tail++] = i;
        }
        foreach (var s in sources)
        {
            var r = s.Cell - nb.Origin;
            if ((uint)r.X >= N || (uint)r.Y >= N || (uint)r.Z >= N) continue;
            int i = Neighborhood.Index(r.X, r.Y, r.Z);
            light[i] = Merge(light[i], s.Light);
            queue[tail++] = i;
        }
        Propagate(light, queue, ref head, ref tail, nb, filter, sky: false);
        return light;
    }

    private static ushort Merge(ushort current, ushort block) =>
        LightValue.Pack(LightValue.Sky(current),
            Math.Max(LightValue.R(current), LightValue.R(block)),
            Math.Max(LightValue.G(current), LightValue.G(block)),
            Math.Max(LightValue.B(current), LightValue.B(block)));

    private static void Propagate(ushort[] light, int[] queue, ref int head, ref int tail, Neighborhood nb, byte[] filter, bool sky)
    {
        int capacity = queue.Length;
        var blocks = nb.Blocks;
        var liquids = nb.Liquids;
        var shapes = nb.HasShapes ? nb.Shapes : null;
        while (head != tail)
        {
            int i = queue[head];
            head = head + 1 == capacity ? 0 : head + 1;
            int x = i % N, z = (i / N) % N, y = i / (N * N);
            ushort cur = light[i];
            for (int f = 0; f < 6; f++)
            {
                int nx = x, ny = y, nz = z;
                switch (f)
                {
                    case 0: nx++; break;
                    case 1: nx--; break;
                    case 2: ny++; break;
                    case 3: ny--; break;
                    case 4: nz++; break;
                    default: nz--; break;
                }
                if ((uint)nx >= N || (uint)ny >= N || (uint)nz >= N) continue;
                int j = (ny * N + nz) * N + nx;
                // Shaped cells (slopes, panels…) are only partly solid and let light through.
                int blockLoss = shapes != null && shapes[j] != 0 ? 0 : filter[blocks[j]];
                int loss = 1 + blockLoss + (liquids[j] != 0 ? 1 : 0);
                if (loss >= 15) continue;
                ushort old = light[j];
                ushort next;
                if (sky)
                {
                    int s = LightValue.Sky(cur) - loss;
                    if (s <= LightValue.Sky(old)) continue;
                    next = (ushort)((old & 0x0FFF) | (s << 12));
                }
                else
                {
                    int r = Math.Max(LightValue.R(old), LightValue.R(cur) - loss);
                    int g = Math.Max(LightValue.G(old), LightValue.G(cur) - loss);
                    int b = Math.Max(LightValue.B(old), LightValue.B(cur) - loss);
                    next = LightValue.Pack(LightValue.Sky(old), r, g, b);
                    if (next == old) continue;
                }
                light[j] = next;
                queue[tail] = j;
                tail = tail + 1 == capacity ? 0 : tail + 1;
            }
        }
    }
}
