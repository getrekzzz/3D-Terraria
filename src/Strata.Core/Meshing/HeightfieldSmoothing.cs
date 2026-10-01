using Strata.Core.Common;

namespace Strata.Core.Meshing;

/// <summary>
/// Rebuilds the sub-cell height of gentle ground from the column heights around each vertex. Surface nets on binary
/// occupancy turn a slope into flat terraces, and Taubin relaxation is a low-pass filter that keeps steps six or more
/// cells long as ripples. Here every free, up-facing vertex on locally heightfield-like ground takes the tent-weighted
/// mean of the column tops around it (a smooth ramp through the staircase), clamped inside its dual cell and within
/// <see cref="SurfaceNetsMesher.MaxDrift"/> of its voxel-faithful centroid, so visuals and colliders keep matching the
/// voxels. Cliffs, overhangs, thin ledges and roofs, and anything near built cells are left to the relaxation.
/// Only a fixed window around the vertex is read (±5 columns, ±5 cells), well inside the neighbourhood margin, so
/// both chunks sharing a border compute the same border vertices.
/// </summary>
internal static class HeightfieldSmoothing
{
    /// <summary>Tent half-width in columns (weights 4,3,2,1 either side of a vertex on its dual cell centre).</summary>
    public const int Radius = 4;

    /// <summary>Columns whose top is further than this from the vertex's dual cell centre are cliffs and ignored.</summary>
    public const int MaxStep = 3;

    private const int Below = 4, Above = 5; // column window [y - Below, y + Above] around dual cell y
    private const int MinValidPercent = 75;
    private const float Inset = 0.02f; // keeps paired step vertices apart (no zero-area slivers)

    private const int D0 = Neighborhood.Margin - 1, D1 = Neighborhood.Margin + ChunkMath.Size; // dual cells the mesher emits
    private const int C0 = D0 - Radius, C1 = D1 + Radius + 1; // columns any of them reads
    private const int CW = C1 - C0;
    private const sbyte Unknown = sbyte.MaxValue, Invalid = sbyte.MinValue;

    /// <summary>Per (dual y, column) cache: column top relative to the dual cell centre, or Invalid.</summary>
    [ThreadStatic] private static sbyte[]? _columns;

    public static void Apply(MeshVolume vol, SurfaceNetsMesher.Scratch s)
    {
        var cache = _columns ??= new sbyte[(D1 - D0) * CW * CW];
        Array.Fill(cache, Unknown);
        Span<float> wx = stackalloc float[Span], wz = stackalloc float[Span];
        for (int y = D0; y < D1; y++)
            for (int z = D0; z < D1; z++)
                for (int x = D0; x < D1; x++)
                {
                    int di = SurfaceNetsMesher.DI(x, y, z);
                    if (s.State[di] != SurfaceNetsMesher.Free || !UpFacing(vol, x, y, z)) continue;
                    var p = s.Pos[di];
                    // Vertex offset from the dual cell centre (small numbers: the same in every chunk's region frame).
                    float fx = p.X - (x + 1), fz = p.Z - (z + 1);
                    for (int k = 0; k < Span; k++)
                    {
                        wx[k] = MathF.Max(0f, Radius + 0.5f - MathF.Abs(k - Radius - 0.5f - fx));
                        wz[k] = MathF.Max(0f, Radius + 0.5f - MathF.Abs(k - Radius - 0.5f - fz));
                    }
                    if (!Reconstruct(vol, cache, x, y, z, wx, wz, out float rel)) continue;
                    float initial = s.Initial[di].Y;
                    s.Pos[di].Y = Math.Clamp(y + 1 + rel, MathF.Max(y + 0.5f + Inset, initial - SurfaceNetsMesher.MaxDrift),
                        MathF.Min(y + 1.5f - Inset, initial + SurfaceNetsMesher.MaxDrift));
                }
    }

    private const int Span = 2 * Radius + 2; // columns x - Radius .. x + Radius + 1 cover every non-zero tent weight

    /// <summary>
    /// Tent-weighted mean column top around dual cell (x, y, z), relative to its centre. False when the vertex's own
    /// four columns are not plain ground (protects ledges, roofs and cliff edges) or under 75% of the 8×8 columns
    /// around it are.
    /// </summary>
    private static bool Reconstruct(MeshVolume vol, sbyte[] cache, int x, int y, int z, Span<float> wx, Span<float> wz, out float rel)
    {
        rel = 0f;
        int layer = (y - D0) * CW * CW;
        for (int k = 0; k < 4; k++)
        {
            int cx = x + (k & 1), cz = z + (k >> 1);
            if (Column(vol, cache, layer + (cz - C0) * CW + cx - C0, cx, cz, y) == Invalid) return false;
        }
        float sum = 0f, weights = 0f;
        int valid = 0;
        for (int kz = 0; kz < Span; kz++)
        {
            int cz = z - Radius + kz;
            int row = layer + (cz - C0) * CW - C0;
            bool innerZ = kz >= 1 && kz <= 2 * Radius;
            float rowSum = 0f, rowWeights = 0f;
            for (int kx = 0; kx < Span; kx++)
            {
                int cx = x - Radius + kx;
                sbyte h = cache[row + cx];
                if (h == Unknown) h = Column(vol, cache, row + cx, cx, cz, y);
                if (h == Invalid) continue;
                if (innerZ && kx >= 1 && kx <= 2 * Radius) valid++;
                rowSum += wx[kx] * h;
                rowWeights += wx[kx];
            }
            sum += wz[kz] * rowSum;
            weights += wz[kz] * rowWeights;
        }
        if (valid * 100 < MinValidPercent * (2 * Radius) * (2 * Radius) || weights <= 0f) return false;
        rel = sum / weights;
        return true;
    }

    /// <summary>
    /// The dual cell's 2×2 sample columns rise monotonically (never opaque above non-opaque), with ground under at
    /// least one of them and air over at least one: a piece of heightfield, not an overhang or ceiling.
    /// </summary>
    private static bool UpFacing(MeshVolume vol, int x, int y, int z)
    {
        var opaque = vol.Opaque;
        int bottom = 0, top = 0;
        for (int k = 0; k < 4; k++)
        {
            int i = MeshVolume.Index(x + (k & 1), y, z + (k >> 1));
            bool b = opaque[i], t = opaque[i + MeshVolume.N * MeshVolume.N];
            if (t && !b) return false;
            if (b) bottom++;
            if (t) top++;
        }
        return bottom >= 1 && top <= 3;
    }

    /// <summary>
    /// Top of column (cx, cz) inside the window around dual cell y, relative to the dual cell centre (y + 1):
    /// solid from the window bottom up to the top, air above it to the window top, no snapping (built, shaped or
    /// see-through) cell, and within <see cref="MaxStep"/>. Otherwise Invalid.
    /// </summary>
    private static sbyte Column(MeshVolume vol, sbyte[] cache, int ci, int cx, int cz, int y)
    {
        sbyte cached = cache[ci];
        if (cached != Unknown) return cached;
        var opaque = vol.Opaque;
        var snap = vol.Snap;
        const int layer = MeshVolume.N * MeshVolume.N;
        int i = MeshVolume.Index(cx, y + Above, cz);
        int top = int.MinValue;
        sbyte result = Invalid;
        for (int cy = y + Above; cy >= y - Below; cy--, i -= layer)
        {
            if (snap[i]) { top = int.MinValue; break; }
            bool o = opaque[i];
            if (top == int.MinValue)
            {
                if (!o) continue;
                if (cy == y + Above) break; // no air above inside the window
                top = cy;
            }
            else if (!o) { top = int.MinValue; break; } // air under the top: a ledge, roof or cave
        }
        if (top != int.MinValue && Math.Abs(top - y) <= MaxStep) result = (sbyte)(top - y);
        cache[ci] = result;
        return result;
    }
}
