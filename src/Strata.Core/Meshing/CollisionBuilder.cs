using System.Numerics;

namespace Strata.Core.Meshing;

/// <summary>
/// Collects collision triangles (chunk-local meters) bucketed into 2×2×2 sub-chunks of 16³ cells, so an
/// edit only rebuilds a small physics shape. Platforms are kept separate (one-way collision layer).
/// </summary>
public sealed class CollisionBuilder
{
    public const int Buckets = 8;
    private const float Half = Common.ChunkMath.Size * Common.Units.CellSize * 0.5f;

    public List<Vector3>[] Solid { get; } = Create();
    public List<Vector3>[] Platforms { get; } = Create();

    private static List<Vector3>[] Create()
    {
        var a = new List<Vector3>[Buckets];
        for (int i = 0; i < Buckets; i++) a[i] = new List<Vector3>();
        return a;
    }

    public static int BucketOf(Vector3 localMeters) =>
        (localMeters.X >= Half ? 1 : 0) | (localMeters.Y >= Half ? 2 : 0) | (localMeters.Z >= Half ? 4 : 0);

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, bool platform = false)
    {
        var list = (platform ? Platforms : Solid)[BucketOf((a + b + c) / 3f)];
        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

    public int TriangleCount => Solid.Sum(l => l.Count) / 3 + Platforms.Sum(l => l.Count) / 3;

    /// <summary>Total hash slots: the solid buckets, then the platform buckets.</summary>
    public const int HashCount = Buckets * 2;

    /// <summary>
    /// Per-bucket geometry hashes from <see cref="ComputeHashes"/> (null until computed). 0 marks an empty bucket, so
    /// colliders rebuild only buckets whose triangles changed and skip liquid- or light-only remeshes entirely.
    /// </summary>
    public ulong[]? Hashes { get; private set; }

    /// <summary>Hashes every bucket's triangle count and vertex float bits (call on the worker, after meshing).</summary>
    public ulong[] ComputeHashes()
    {
        var hashes = new ulong[HashCount];
        for (int i = 0; i < Buckets; i++)
        {
            hashes[i] = HashBucket(Solid[i], 1);
            hashes[Buckets + i] = HashBucket(Platforms[i], 2);
        }
        Hashes = hashes;
        return hashes;
    }

    private static ulong HashBucket(List<Vector3> tris, ulong salt)
    {
        if (tris.Count == 0) return 0;
        ulong h = Determinism.Hash.Combine(salt, (ulong)tris.Count);
        foreach (var v in tris)
        {
            ulong xy = BitConverter.SingleToUInt32Bits(v.X) | (ulong)BitConverter.SingleToUInt32Bits(v.Y) << 32;
            h = Determinism.Hash.Combine(Determinism.Hash.Combine(h, xy), (ulong)BitConverter.SingleToUInt32Bits(v.Z));
        }
        return h == 0 ? 1 : h;
    }
}
