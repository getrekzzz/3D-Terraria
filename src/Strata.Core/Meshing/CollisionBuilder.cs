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
}
