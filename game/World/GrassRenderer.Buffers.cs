using Godot;
using Strata.Core.Common;
using Strata.Core.Meshing;

namespace Strata.Game.World;

/// <summary>Instance buffers for chunk grass, written into pooled scratch and uploaded into reused multimeshes.</summary>
public sealed partial class GrassRenderer
{
    private const int FloatsPerInstance = 20;   // 3×4 transform, colour, custom data

    private float[] _clumpScratch = System.Array.Empty<float>(), _flowerScratch = System.Array.Empty<float>();

    /// <summary>
    /// Writes a chunk's clump and flower buffers. A live chunk keeps its multimeshes (reallocated only when the count
    /// changed); otherwise they are created.
    /// </summary>
    private void Build(Int3 coord, ChunkGrass chunk)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.grass.create");
        int count = chunk.Count, flowerCount = chunk.Flowers;
        if (_clumpScratch.Length < count * FloatsPerInstance) _clumpScratch = new float[count * FloatsPerInstance];
        if (_flowerScratch.Length < flowerCount * FloatsPerInstance) _flowerScratch = new float[flowerCount * FloatsPerInstance];
        int f = 0;
        for (int i = 0; i < count; i++)
        {
            var c = chunk.Clumps[i];
            var basis = ClumpBasis(c);
            var root = ToGodot(c.Position) - basis.Y.Normalized() * 0.03f;
            float jitter = 0.86f + 0.28f * (c.Shade / 255f);
            var tint = _kindColors[c.Kind] * new Vector3(jitter, jitter * (0.97f + 0.06f * ((c.Shade * 7) & 255) / 255f), jitter * 0.95f);
            var light = Light(c.Light);
            Write(_clumpScratch, i, basis, root, new Color(tint.X, tint.Y, tint.Z, _kindGlow[c.Kind]), light);
            if (c.Flower == 0) continue;
            var flowerBasis = basis.Scaled(new Vector3(1f, 1.15f, 1f));
            Write(_flowerScratch, f++, flowerBasis, root, _flowerColors[c.Kind][c.Flower - 1], light);
        }

        if (!chunk.Live)
        {
            chunk.Multimesh = CreateMultimesh(_near);
            chunk.Instance = CreateInstance(chunk.Multimesh, _material, coord);
            chunk.Near = true;
        }
        if (chunk.Allocated != count)
        {
            Allocate(chunk.Multimesh, count);
            chunk.Allocated = count;
            chunk.Visible = -1;
        }
        RenderingServer.MultimeshSetBuffer(chunk.Multimesh, new System.ReadOnlySpan<float>(_clumpScratch, 0, count * FloatsPerInstance));
        if (chunk.LiveRanks.Length < count) chunk.LiveRanks = new float[count];
        for (int i = 0; i < count; i++) chunk.LiveRanks[i] = chunk.Clumps[i].Rank;

        if (flowerCount > 0)
        {
            if (!chunk.FlowerMultimesh.IsValid)
            {
                chunk.FlowerMultimesh = CreateMultimesh(_flower);
                chunk.FlowerInstance = CreateInstance(chunk.FlowerMultimesh, _flowerMaterial, coord);
            }
            if (chunk.FlowersAllocated != flowerCount)
            {
                Allocate(chunk.FlowerMultimesh, flowerCount);
                chunk.FlowersAllocated = flowerCount;
            }
            RenderingServer.MultimeshSetBuffer(chunk.FlowerMultimesh, new System.ReadOnlySpan<float>(_flowerScratch, 0, flowerCount * FloatsPerInstance));
        }
        else ReleaseFlowers(chunk);
        chunk.NeedsBuild = chunk.Reshaped = false;
    }

    /// <summary>Clump orientation: mostly upright (grass grows toward the light), yawed, scaled to its height.</summary>
    private static Basis ClumpBasis(GrassClump c)
    {
        var up = ToGodot(c.Normal).Lerp(Vector3.Up, 0.7f).Normalized();
        var yawed = new Basis(Vector3.Up, c.Yaw);
        var tilt = new Basis(new Quaternion(Vector3.Up, up));
        float spread = 0.75f + 0.7f * c.Height;
        return (tilt * yawed).Scaled(new Vector3(spread, c.Height, spread));
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Color Light(uint packed) =>
        new((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, (packed >> 24) / 255f);

    private static void Write(float[] buffer, int index, Basis b, Vector3 origin, Color color, Color custom)
    {
        int o = index * FloatsPerInstance;
        buffer[o + 0] = b.X.X; buffer[o + 1] = b.Y.X; buffer[o + 2] = b.Z.X; buffer[o + 3] = origin.X;
        buffer[o + 4] = b.X.Y; buffer[o + 5] = b.Y.Y; buffer[o + 6] = b.Z.Y; buffer[o + 7] = origin.Y;
        buffer[o + 8] = b.X.Z; buffer[o + 9] = b.Y.Z; buffer[o + 10] = b.Z.Z; buffer[o + 11] = origin.Z;
        buffer[o + 12] = color.R; buffer[o + 13] = color.G; buffer[o + 14] = color.B; buffer[o + 15] = color.A;
        buffer[o + 16] = custom.R; buffer[o + 17] = custom.G; buffer[o + 18] = custom.B; buffer[o + 19] = custom.A;
    }

    private static readonly Aabb Bounds = new(new Vector3(-1f, -1f, -1f), Vector3.One * (ChunkMeters + 2f));

    private static Rid CreateMultimesh(Mesh mesh)
    {
        var mm = RenderingServer.MultimeshCreate();
        RenderingServer.MultimeshSetMesh(mm, mesh.GetRid());
        return mm;
    }

    private static void Allocate(Rid multimesh, int count)
    {
        RenderingServer.MultimeshAllocateData(multimesh, count, RenderingServer.MultimeshTransformFormat.Transform3D, true, true);
        RenderingServer.MultimeshSetCustomAabb(multimesh, Bounds);
    }

    private Rid CreateInstance(Rid multimesh, Material material, Int3 coord)
    {
        var instance = RenderingServer.InstanceCreate2(multimesh, GetWorld3D().Scenario);
        RenderingServer.InstanceSetTransform(instance, new Transform3D(Basis.Identity, new Vector3(coord.X, coord.Y, coord.Z) * ChunkMeters));
        RenderingServer.InstanceGeometrySetMaterialOverride(instance, material.GetRid());
        RenderingServer.InstanceGeometrySetCastShadowsSetting(instance, RenderingServer.ShadowCastingSetting.Off);
        return instance;
    }

    private static void ReleaseFlowers(ChunkGrass chunk)
    {
        if (!chunk.FlowerMultimesh.IsValid) return;
        RenderingServer.FreeRid(chunk.FlowerInstance);
        RenderingServer.FreeRid(chunk.FlowerMultimesh);
        chunk.FlowerInstance = chunk.FlowerMultimesh = default;
        chunk.FlowersAllocated = 0;
    }

    private static void Release(ChunkGrass chunk)
    {
        if (!chunk.Live) return;
        RenderingServer.FreeRid(chunk.Instance);
        RenderingServer.FreeRid(chunk.Multimesh);
        ReleaseFlowers(chunk);
        chunk.Instance = chunk.Multimesh = default;
        chunk.Allocated = 0;
        chunk.Visible = -1;
    }
}
