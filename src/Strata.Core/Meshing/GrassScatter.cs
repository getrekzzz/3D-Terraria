using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;

namespace Strata.Core.Meshing;

/// <summary>
/// One clump of grass blades: chunk-local position in metres, the surface normal it grows from, its facing and
/// height, grass kind (index into <see cref="Presentation.GrassConfig.Ordered"/>), flower (0 none, else colour
/// index + 1), a random shade byte for colour variation, and the voxel light at its root (RGBA8 like the mesh).
/// </summary>
public readonly record struct GrassClump(Vector3 Position, Vector3 Normal, float Yaw, float Height, byte Kind, byte Flower, byte Shade, uint Light);

/// <summary>
/// Scatters grass clumps over the upward faces of grass surfaces in a chunk mesh. Placement is a pure function of
/// world position (each triangle is seeded by its centroid), so a chunk regrows the same grass after every rebuild
/// and edits only disturb the grass they touch. Clumps thin out across material borders the way the terrain
/// textures blend, stay off steep faces, and never grow into a block above. The output is shuffled: any prefix is
/// an even thinning of the whole chunk, which the renderer uses for distance LOD.
/// </summary>
public static class GrassScatter
{
    /// <summary>Upper bound per chunk (keeps a fully grassed chunk's instance buffer bounded).</summary>
    public const int MaxClumps = 16384;

    private const ulong Salt = 0x6772617373UL;

    public static void Build(MeshVolume vol, MeshSurface surface, List<GrassClump> output)
    {
        var grass = vol.Tables.Grass;
        if (grass == null || surface.IsEmpty) return;
        var kinds = grass.Ordered;
        var surfaceKind = vol.Tables.GrassKind;
        var worldOrigin = ChunkMath.ChunkOrigin(vol.Nb.Chunk);
        var origin = new Vector3(worldOrigin.X, worldOrigin.Y, worldOrigin.Z) * Units.CellSize;
        var pos = surface.Positions;
        var idx = surface.Indices;
        Span<int> kind = stackalloc int[3];
        for (int t = 0; t + 2 < idx.Count && output.Count < MaxClumps; t += 3)
        {
            int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
            ulong ids = surface.Custom0[i0];
            float maxDensity = 0f;
            for (int c = 0; c < 3; c++)
            {
                int s = (int)(float)BitConverter.UInt16BitsToHalf((ushort)(ids >> (16 * c)));
                kind[c] = s >= 0 && s < surfaceKind.Length ? surfaceKind[s] : -1;
                if (kind[c] >= 0) maxDensity = MathF.Max(maxDensity, kinds[kind[c]].Value.Density);
            }
            if (maxDensity <= 0f) continue;
            Vector3 a = pos[i0], b = pos[i1], c2 = pos[i2];
            var cross = Vector3.Cross(b - a, c2 - a);
            float len = cross.Length();
            if (len < 1e-8f) continue;
            var n = cross / len;
            if (Vector3.Dot(n, surface.Normals[i0] + surface.Normals[i1] + surface.Normals[i2]) < 0f) n = -n;
            if (n.Y < grass.MinUpward) continue;
            float slope = Math.Clamp((n.Y - grass.MinUpward) / 0.2f, 0f, 1f);
            float expected = 0.5f * len * grass.ClumpsPerSquareMeter * maxDensity * slope;
            var centre = origin + (a + b + c2) / 3f;
            ulong h = Hash.Cell(Salt, (int)MathF.Round(centre.X * 16f), (int)MathF.Round(centre.Y * 16f), (int)MathF.Round(centre.Z * 16f));
            int count = (int)expected;
            if (Hash.ToUnitFloat(h) < expected - count) count++;
            for (int k = 0; k < count && output.Count < MaxClumps; k++)
            {
                ulong r = Hash.Combine(h, k);
                float sq = MathF.Sqrt(Hash.ToUnitFloat(Hash.Mix(r ^ 1)));
                float r2 = Hash.ToUnitFloat(Hash.Mix(r ^ 2));
                float u = 1f - sq, v = sq * (1f - r2), w = sq * r2;
                var weights = Weights(surface, i0, i1, i2, u, v, w);
                int best = -1;
                float grassWeight = 0f, bestWeight = 0f;
                for (int c = 0; c < 3; c++)
                {
                    if (kind[c] < 0) continue;
                    float wc = Component(weights, c);
                    grassWeight += wc * kinds[kind[c]].Value.Density / maxDensity;
                    if (wc > bestWeight) (bestWeight, best) = (wc, c);
                }
                if (best < 0 || Hash.ToUnitFloat(Hash.Mix(r ^ 3)) >= SmoothStep(0.3f, 0.7f, grassWeight)) continue;
                var p = a * u + b * v + c2 * w;
                if (!OpenAbove(vol, p, n)) continue;
                int kindIndex = kind[best];
                var look = kinds[kindIndex].Value;
                float height = look.MinHeight + (look.MaxHeight - look.MinHeight) * Hash.ToUnitFloat(Hash.Mix(r ^ 4));
                float yaw = Hash.ToUnitFloat(Hash.Mix(r ^ 5)) * MathF.Tau;
                byte flower = 0;
                if (look.Flowers > 0f && Hash.ToUnitFloat(Hash.Mix(r ^ 6)) < look.Flowers)
                    flower = (byte)(1 + Hash.ToRange(Hash.Mix(r ^ 7), look.FlowerColors.Length));
                var light = LerpLight(surface.Custom1[i0], surface.Custom1[i1], surface.Custom1[i2], u, v, w);
                output.Add(new GrassClump(p, n, yaw, height, (byte)kindIndex, flower, (byte)(Hash.Mix(r ^ 8) >> 56), light));
            }
        }
        Shuffle(output, Hash.Cell(Salt, worldOrigin.X, worldOrigin.Y, worldOrigin.Z));
    }

    /// <summary>
    /// Hashes of scattered grass: <c>Shape</c> covers what the clumps look like and where they stand (count,
    /// positions, normals, facing, height, kinds, flowers), <c>Light</c> only their root light. A remesh with the
    /// same shape but different light can rewrite the instance buffer in place; the same both needs no work.
    /// </summary>
    public static (ulong Shape, ulong Light) Hashes(List<GrassClump> clumps)
    {
        ulong shape = Hash.Combine(Salt, clumps.Count), light = Hash.Combine(Salt ^ 1, clumps.Count);
        foreach (var c in clumps)
        {
            shape = Hash.Combine(shape, Bits(c.Position.X) | (ulong)Bits(c.Position.Y) << 32);
            shape = Hash.Combine(shape, Bits(c.Position.Z) | (ulong)Bits(c.Normal.X) << 32);
            shape = Hash.Combine(shape, Bits(c.Normal.Y) | (ulong)Bits(c.Normal.Z) << 32);
            shape = Hash.Combine(shape, Bits(c.Yaw) | (ulong)Bits(c.Height) << 32);
            shape = Hash.Combine(shape, c.Kind | (ulong)c.Flower << 8 | (ulong)c.Shade << 16);
            light = Hash.Combine(light, c.Light);
        }
        return (shape, light);
    }

    private static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

    /// <summary>Material weights at a point: the vertex colours carry per-corner material barycentrics.</summary>
    private static Vector3 Weights(MeshSurface surface, int i0, int i1, int i2, float u, float v, float w)
    {
        var c0 = surface.Colors[i0];
        var c1 = surface.Colors[i1];
        var c2 = surface.Colors[i2];
        return new Vector3(c0.X, c0.Y, c0.Z) * u + new Vector3(c1.X, c1.Y, c1.Z) * v + new Vector3(c2.X, c2.Y, c2.Z) * w;
    }

    private static float Component(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>True when the cell just above the point (along the normal) is empty: no glass, block or shape.</summary>
    private static bool OpenAbove(MeshVolume vol, Vector3 local, Vector3 normal)
    {
        var q = local / Units.CellSize + new Vector3(Neighborhood.Margin) + normal * 0.5f;
        int x = (int)MathF.Floor(q.X), y = (int)MathF.Floor(q.Y), z = (int)MathF.Floor(q.Z);
        if ((uint)x >= Neighborhood.Size || (uint)y >= Neighborhood.Size || (uint)z >= Neighborhood.Size) return false;
        int i = Neighborhood.Index(x, y, z);
        return vol.Tables.Class[vol.Nb.Blocks[i]] == CellClass.Empty && !(vol.Nb.HasShapes && vol.Nb.Shapes[i] != 0);
    }

    private static uint LerpLight(uint l0, uint l1, uint l2, float u, float v, float w)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float x = ((l0 >> shift) & 255) * u + ((l1 >> shift) & 255) * v + ((l2 >> shift) & 255) * w;
            result |= (uint)Math.Clamp((int)(x + 0.5f), 0, 255) << shift;
        }
        return result;
    }

    private static void Shuffle(List<GrassClump> list, ulong seed)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            seed = Hash.Mix(seed);
            int j = Hash.ToRange(seed, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
