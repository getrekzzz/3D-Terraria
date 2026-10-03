using System.Numerics;
using Strata.Core.Common;

namespace Strata.Core.Meshing;

/// <summary>
/// Constrained surface nets over the opaque/non-opaque boundary of the voxel grid:
/// one vertex per dual cell, relaxed (Laplacian) but clamped inside its dual cell so the surface never
/// strays more than half a cell from the voxels (visuals and collision agree, single blocks never vanish).
/// Dual cells touching crisp/shaped/transparent cells snap to grid corners, so built blocks get exact
/// flat faces that meet smooth terrain watertight. Triangles with mixed materials carry all three
/// surface ids with barycentric weights so the shader blends seamlessly.
/// </summary>
public static class SurfaceNetsMesher
{
    /// <summary>Taubin smoothing steps (alternating shrink λ and inflate μ so features keep their volume).</summary>
    public const int RelaxIterations = 8;
    private const float Lambda = 0.5f, Mu = -0.53f;

    /// <summary>Maximum distance (cells, per axis) relaxation may move a vertex from its edge centroid.</summary>
    public const float MaxDrift = 0.45f;
    private const int Lo = Neighborhood.Margin - 1 - RelaxIterations;
    private const int Hi = Neighborhood.Margin + ChunkMath.Size + RelaxIterations;
    private const int D = Hi - Lo;
    private const byte None = 0, Free = 1, Snapped = 2;

    private sealed class Scratch
    {
        public readonly Vector3[] Pos = new Vector3[D * D * D];
        public readonly Vector3[] Tmp = new Vector3[D * D * D];
        public readonly Vector3[] Initial = new Vector3[D * D * D];
        public readonly byte[] State = new byte[D * D * D];
        public readonly int[] OutIndex = new int[D * D * D];
        public readonly ushort[] Material = new ushort[D * D * D];
        public readonly byte[] Paint = new byte[D * D * D];
        public readonly byte[] Density = new byte[Neighborhood.Volume];
    }

    [ThreadStatic] private static Scratch? _scratch;

    private static int DI(int x, int y, int z) => ((y - Lo) * D + (z - Lo)) * D + (x - Lo);

    public static void Build(MeshVolume vol, MeshSurface surface, CollisionBuilder collision)
    {
        var s = _scratch ??= new Scratch();
        Array.Clear(s.State);
        Array.Fill(s.OutIndex, -1);
        PlaceVertices(vol, s);
        Relax(s);
        ComputeDensity(vol, s.Density);
        EmitQuads(vol, s, surface, collision);
    }

    private static void PlaceVertices(MeshVolume vol, Scratch s)
    {
        var opaque = vol.Opaque;
        var snap = vol.Snap;
        for (int y = Lo; y < Hi; y++)
            for (int z = Lo; z < Hi; z++)
                for (int x = Lo; x < Hi; x++)
                {
                    int mask = 0;
                    bool snapped = false;
                    for (int k = 0; k < 8; k++)
                    {
                        int i = MeshVolume.Index(x + (k & 1), y + ((k >> 1) & 1), z + (k >> 2));
                        if (opaque[i]) mask |= 1 << k;
                        snapped |= snap[i];
                    }
                    if (mask == 0 || mask == 0xFF) continue;
                    int di = DI(x, y, z);
                    if (snapped)
                    {
                        s.Pos[di] = new Vector3(x + 1, y + 1, z + 1);
                        s.State[di] = Snapped;
                    }
                    else
                    {
                        s.Pos[di] = EdgeCentroid(x, y, z, mask);
                        s.Initial[di] = s.Pos[di];
                        s.State[di] = Free;
                    }
                    s.Material[di] = Majority(vol, x, y, z);
                    s.Paint[di] = (byte)CornerPaint(vol, x, y, z);
                }
    }

    private static readonly (int A, int B)[] Edges =
    {
        (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7),
    };

    private static Vector3 EdgeCentroid(int x, int y, int z, int mask)
    {
        var sum = Vector3.Zero;
        int n = 0;
        foreach (var (a, b) in Edges)
        {
            if (((mask >> a) & 1) == ((mask >> b) & 1)) continue;
            sum += (Corner(a) + Corner(b)) * 0.5f;
            n++;
        }
        return new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) + sum / n;
    }

    private static Vector3 Corner(int k) => new(k & 1, (k >> 1) & 1, k >> 2);

    /// <summary>
    /// Dominant opaque block among the 8 samples, weighted towards samples exposed to air above them
    /// (so grass tops win over the dirt beneath at terrace edges) and towards upper samples.
    /// </summary>
    /// <summary>Paint bits of a smooth vertex: the paint of a painted solid corner, preferring exposed ones.</summary>
    private static int CornerPaint(MeshVolume vol, int x, int y, int z)
    {
        if (!vol.Nb.HasFlags) return 0;
        int best = 0;
        for (int k = 0; k < 8; k++)
        {
            int i = MeshVolume.Index(x + (k & 1), y + ((k >> 1) & 1), z + (k >> 2));
            int bits = vol.Opaque[i] ? vol.PaintBits(i) : 0;
            if (bits == 0) continue;
            if (!vol.Opaque[i + MeshVolume.N * MeshVolume.N]) return bits;
            best = bits;
        }
        return best;
    }

    private static ushort Majority(MeshVolume vol, int x, int y, int z)
    {
        Span<ushort> blocks = stackalloc ushort[8];
        Span<int> score = stackalloc int[8];
        int n = 0;
        for (int k = 0; k < 8; k++)
        {
            int sy = y + ((k >> 1) & 1);
            int i = MeshVolume.Index(x + (k & 1), sy, z + (k >> 2));
            if (!vol.Opaque[i]) continue;
            ushort b = vol.Nb.Blocks[i];
            bool exposed = !vol.Opaque[i + MeshVolume.N * MeshVolume.N];
            int weight = 2 + ((k >> 1) & 1) + (exposed ? 6 : 0);
            int slot = blocks[..n].IndexOf(b);
            if (slot < 0)
            {
                blocks[n] = b;
                score[n++] = weight;
            }
            else score[slot] += weight;
        }
        int best = 0;
        for (int i = 1; i < n; i++)
            if (score[i] > score[best]) best = i;
        return vol.Tables.Surface[blocks[best]];
    }

    private static void Relax(Scratch s)
    {
        for (int it = 0; it < RelaxIterations; it++)
        {
            Array.Copy(s.Pos, s.Tmp, s.Pos.Length);
            for (int y = Lo + 1; y < Hi - 1; y++)
                for (int z = Lo + 1; z < Hi - 1; z++)
                    for (int x = Lo + 1; x < Hi - 1; x++)
                    {
                        int di = DI(x, y, z);
                        if (s.State[di] != Free) continue;
                        var sum = Vector3.Zero;
                        int n = 0;
                        Accumulate(s, DI(x + 1, y, z), ref sum, ref n);
                        Accumulate(s, DI(x - 1, y, z), ref sum, ref n);
                        Accumulate(s, DI(x, y + 1, z), ref sum, ref n);
                        Accumulate(s, DI(x, y - 1, z), ref sum, ref n);
                        Accumulate(s, DI(x, y, z + 1), ref sum, ref n);
                        Accumulate(s, DI(x, y, z - 1), ref sum, ref n);
                        if (n == 0) continue;
                        float factor = (it & 1) == 0 ? Lambda : Mu;
                        var target = s.Tmp[di] + (sum / n - s.Tmp[di]) * factor;
                        // Stay near the voxel-faithful centroid (thin features keep their size) and inside the dual cell.
                        var initial = s.Initial[di];
                        target = Vector3.Clamp(target, initial - new Vector3(MaxDrift), initial + new Vector3(MaxDrift));
                        var min = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        s.Pos[di] = Vector3.Clamp(target, min, min + Vector3.One);
                    }
        }
    }

    private static void Accumulate(Scratch s, int di, ref Vector3 sum, ref int n)
    {
        if (s.State[di] == None) return;
        sum += s.Tmp[di];
        n++;
    }

    /// <summary>3×3×3 box count of opaque samples (separable sums): a smooth density used for vertex normals.</summary>
    private static void ComputeDensity(MeshVolume vol, byte[] density)
    {
        const int N = MeshVolume.N;
        var a = _sumA ??= new byte[Neighborhood.Volume];
        var b = _sumB ??= new byte[Neighborhood.Volume];
        var opaque = vol.Opaque;
        for (int i = 1; i < Neighborhood.Volume - 1; i++)
            a[i] = (byte)((opaque[i - 1] ? 1 : 0) + (opaque[i] ? 1 : 0) + (opaque[i + 1] ? 1 : 0));
        for (int i = N; i < Neighborhood.Volume - N; i++) b[i] = (byte)(a[i - N] + a[i] + a[i + N]);
        const int L = N * N;
        for (int i = L; i < Neighborhood.Volume - L; i++) a[i] = (byte)(b[i - L] + b[i] + b[i + L]);
        // Second box pass (values up to 27*27 overflow bytes, so scale down first): a smoother tent kernel.
        var c = _sumC ??= new ushort[Neighborhood.Volume];
        var d = _sumD ??= new ushort[Neighborhood.Volume];
        for (int i = 1; i < Neighborhood.Volume - 1; i++) c[i] = (ushort)(a[i - 1] + a[i] + a[i + 1]);
        for (int i = N; i < Neighborhood.Volume - N; i++) d[i] = (ushort)(c[i - N] + c[i] + c[i + N]);
        for (int i = L; i < Neighborhood.Volume - L; i++) density[i] = (byte)((d[i - L] + d[i] + d[i + L]) / 3);
    }

    [ThreadStatic] private static ushort[]? _sumC, _sumD;

    [ThreadStatic] private static byte[]? _sumA, _sumB;

    private static Vector3 Normal(byte[] density, int x, int y, int z, Vector3 p)
    {
        const int N = MeshVolume.N;
        float tx = p.X - (x + 0.5f), ty = p.Y - (y + 0.5f), tz = p.Z - (z + 0.5f);
        var g = Vector3.Zero;
        for (int k = 0; k < 8; k++)
        {
            int dx = k & 1, dy = (k >> 1) & 1, dz = k >> 2;
            float d = density[((y + dy) * N + (z + dz)) * N + (x + dx)];
            float wx = dx == 1 ? tx : 1 - tx, wy = dy == 1 ? ty : 1 - ty, wz = dz == 1 ? tz : 1 - tz;
            g.X += d * (dx == 1 ? 1 : -1) * wy * wz;
            g.Y += d * (dy == 1 ? 1 : -1) * wx * wz;
            g.Z += d * (dz == 1 ? 1 : -1) * wx * wy;
        }
        float len = g.Length();
        return len < 1e-5f ? Vector3.UnitY : -g / len;
    }

    private static void EmitQuads(MeshVolume vol, Scratch s, MeshSurface surface, CollisionBuilder collision)
    {
        const int c0 = Neighborhood.Margin, c1 = Neighborhood.Margin + ChunkMath.Size;
        Span<int> duals = stackalloc int[4];
        Span<(int X, int Y, int Z)> dualCells = stackalloc (int, int, int)[4];
        for (int y = c0; y < c1; y++)
            for (int z = c0; z < c1; z++)
                for (int x = c0; x < c1; x++)
                {
                    int p = MeshVolume.Index(x, y, z);
                    bool op = vol.Opaque[p];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int qx = x + (axis == 0 ? 1 : 0), qy = y + (axis == 1 ? 1 : 0), qz = z + (axis == 2 ? 1 : 0);
                        int q = MeshVolume.Index(qx, qy, qz);
                        if (op == vol.Opaque[q]) continue;
                        int solid = op ? p : q;
                        var normal = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ } * (op ? 1f : -1f);
                        switch (axis)
                        {
                            case 0:
                                dualCells[0] = (x, y - 1, z - 1); dualCells[1] = (x, y, z - 1); dualCells[2] = (x, y, z); dualCells[3] = (x, y - 1, z);
                                break;
                            case 1:
                                dualCells[0] = (x - 1, y, z - 1); dualCells[1] = (x - 1, y, z); dualCells[2] = (x, y, z); dualCells[3] = (x, y, z - 1);
                                break;
                            default:
                                dualCells[0] = (x - 1, y - 1, z); dualCells[1] = (x, y - 1, z); dualCells[2] = (x, y, z); dualCells[3] = (x - 1, y, z);
                                break;
                        }
                        bool crisp = vol.CrispCell[solid];
                        if (crisp) EmitCrispQuad(vol, s, surface, collision, dualCells, normal, vol.Tables.Surface[vol.Nb.Blocks[solid]], vol.PaintBits(solid));
                        else EmitSmoothQuad(vol, s, surface, collision, dualCells, duals, normal);
                    }
                }
    }

    private static void EmitCrispQuad(MeshVolume vol, Scratch s, MeshSurface surface, CollisionBuilder collision,
        Span<(int X, int Y, int Z)> cells, Vector3 normal, ushort material, int paintBits)
    {
        Span<int> v = stackalloc int[4];
        ulong ids = MeshSurface.PackIds(material, material, material, MeshSurface.FlagCrisp | paintBits);
        for (int k = 0; k < 4; k++)
        {
            var (x, y, z) = cells[k];
            var pos = s.Pos[DI(x, y, z)];
            var color = new Vector4(1, 0, 0, vol.DualOcclusion(x, y, z));
            v[k] = surface.AddVertex(MeshVolume.ToLocalMeters(pos), normal, color, ids, vol.DualLight(x, y, z));
        }
        AddQuad(surface, collision, v, normal);
    }

    private static void EmitSmoothQuad(MeshVolume vol, Scratch s, MeshSurface surface, CollisionBuilder collision,
        Span<(int X, int Y, int Z)> cells, Span<int> v, Vector3 normal)
    {
        for (int k = 0; k < 4; k++)
        {
            var (x, y, z) = cells[k];
            int di = DI(x, y, z);
            if (s.OutIndex[di] < 0)
            {
                var pos = s.Pos[di];
                var n = Normal(s.Density, x, y, z, pos);
                if (Vector3.Dot(n, normal) < -0.2f) n = normal;
                ushort m = s.Material[di];
                s.OutIndex[di] = surface.AddVertex(MeshVolume.ToLocalMeters(pos), n, new Vector4(1, 0, 0, vol.DualOcclusion(x, y, z)),
                    MeshSurface.PackIds(m, m, m, s.Paint[di]), vol.DualLight(x, y, z));
            }
            v[k] = s.OutIndex[di];
        }

        // Split along the shorter diagonal for better shape. Near ties (flat ground, where the two are equal) keep the
        // first one: rounding noise from an edit metres away would otherwise flip quads all around it, and the grass
        // seeded from their triangles with them.
        var p0 = surface.Positions[v[0]];
        var p1 = surface.Positions[v[1]];
        var p2 = surface.Positions[v[2]];
        var p3 = surface.Positions[v[3]];
        bool flip = Vector3.DistanceSquared(p1, p3) < Vector3.DistanceSquared(p0, p2) * 0.99f;
        Span<int> t1 = stackalloc int[3];
        Span<int> t2 = stackalloc int[3];
        if (flip)
        {
            t1[0] = v[0]; t1[1] = v[1]; t1[2] = v[3];
            t2[0] = v[1]; t2[1] = v[2]; t2[2] = v[3];
        }
        else
        {
            t1[0] = v[0]; t1[1] = v[1]; t1[2] = v[2];
            t2[0] = v[0]; t2[1] = v[2]; t2[2] = v[3];
        }
        EmitSmoothTriangle(surface, collision, t1, normal);
        EmitSmoothTriangle(surface, collision, t2, normal);
    }

    private static void EmitSmoothTriangle(MeshSurface surface, CollisionBuilder collision, Span<int> t, Vector3 normal)
    {
        OrientClockwise(surface, t, normal);
        collision.AddTriangle(surface.Positions[t[0]], surface.Positions[t[1]], surface.Positions[t[2]]);
        ulong m0 = surface.Custom0[t[0]], m1 = surface.Custom0[t[1]], m2 = surface.Custom0[t[2]];
        if (m0 == m1 && m1 == m2)
        {
            surface.AddTriangle(t[0], t[1], t[2]);
            return;
        }
        // Mixed materials: private vertices carrying all three ids + barycentric weights.
        ulong ids = (m0 & 0xFFFF) | ((m1 & 0xFFFF) << 16) | ((m2 & 0xFFFF) << 32);
        Span<int> nv = stackalloc int[3];
        for (int k = 0; k < 3; k++)
        {
            int src = t[k];
            var col = surface.Colors[src];
            var bary = new Vector4(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0, col.W);
            nv[k] = surface.AddVertex(surface.Positions[src], surface.Normals[src], bary, ids, surface.Custom1[src]);
        }
        surface.AddTriangle(nv[0], nv[1], nv[2]);
    }

    private static void AddQuad(MeshSurface surface, CollisionBuilder collision, Span<int> v, Vector3 normal)
    {
        Span<int> t = stackalloc int[3];
        t[0] = v[0]; t[1] = v[1]; t[2] = v[2];
        OrientClockwise(surface, t, normal);
        surface.AddTriangle(t[0], t[1], t[2]);
        collision.AddTriangle(surface.Positions[t[0]], surface.Positions[t[1]], surface.Positions[t[2]]);
        t[0] = v[0]; t[1] = v[2]; t[2] = v[3];
        OrientClockwise(surface, t, normal);
        surface.AddTriangle(t[0], t[1], t[2]);
        collision.AddTriangle(surface.Positions[t[0]], surface.Positions[t[1]], surface.Positions[t[2]]);
    }

    /// <summary>Godot treats clockwise triangles (seen from the front) as front faces.</summary>
    internal static void OrientClockwise(MeshSurface surface, Span<int> t, Vector3 normal)
    {
        var a = surface.Positions[t[0]];
        var n = Vector3.Cross(surface.Positions[t[1]] - a, surface.Positions[t[2]] - a);
        if (Vector3.Dot(n, normal) > 0) (t[1], t[2]) = (t[2], t[1]);
    }
}
