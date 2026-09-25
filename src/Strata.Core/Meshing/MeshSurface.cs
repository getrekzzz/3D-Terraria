using System.Numerics;
using System.Runtime.InteropServices;

namespace Strata.Core.Meshing;

/// <summary>
/// Vertex/index buffers for one mesh surface in engine-ready layout:
/// position/normal (float3), color (float4: barycentric rgb + ambient occlusion),
/// custom0 (4 × half: three surface ids + flags) and custom1 (RGBA8: block light rgb + sky light).
/// </summary>
public sealed class MeshSurface
{
    public const int FlagCrisp = 1;
    public const int FlagLiquid = 2;

    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector4> Colors { get; } = new();
    public List<ulong> Custom0 { get; } = new();
    public List<uint> Custom1 { get; } = new();
    public List<int> Indices { get; } = new();

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;
    public bool IsEmpty => Indices.Count == 0;

    public int AddVertex(Vector3 position, Vector3 normal, Vector4 color, ulong custom0, uint custom1)
    {
        Positions.Add(position);
        Normals.Add(normal);
        Colors.Add(color);
        Custom0.Add(custom0);
        Custom1.Add(custom1);
        return Positions.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    /// <summary>Packs three surface ids and flags as half floats (exact for integers ≤ 2048).</summary>
    public static ulong PackIds(int a, int b, int c, int flags) =>
        HalfBits(a) | ((ulong)HalfBits(b) << 16) | ((ulong)HalfBits(c) << 32) | ((ulong)HalfBits(flags) << 48);

    private static ushort HalfBits(int v) => BitConverter.HalfToUInt16Bits((Half)v);

    /// <summary>Packs light (0–15 channels) into RGBA8.</summary>
    public static uint PackLight(float r, float g, float b, float sky) =>
        (uint)Math.Clamp((int)(r * 17f + 0.5f), 0, 255)
        | ((uint)Math.Clamp((int)(g * 17f + 0.5f), 0, 255) << 8)
        | ((uint)Math.Clamp((int)(b * 17f + 0.5f), 0, 255) << 16)
        | ((uint)Math.Clamp((int)(sky * 17f + 0.5f), 0, 255) << 24);

    public ReadOnlySpan<Vector3> PositionSpan => CollectionsMarshal.AsSpan(Positions);
    public ReadOnlySpan<Vector3> NormalSpan => CollectionsMarshal.AsSpan(Normals);
    public ReadOnlySpan<Vector4> ColorSpan => CollectionsMarshal.AsSpan(Colors);
    public ReadOnlySpan<int> IndexSpan => CollectionsMarshal.AsSpan(Indices);

    public byte[] Custom0Bytes() => MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(Custom0)).ToArray();
    public byte[] Custom1Bytes() => MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(Custom1)).ToArray();
}
