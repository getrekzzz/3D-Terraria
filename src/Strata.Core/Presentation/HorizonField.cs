using System.Numerics;
using Strata.Core.Common;
using Strata.Core.WorldGen;

namespace Strata.Core.Presentation;

/// <summary>One point of the far view: ground (or sea) height in metres, its colour (sRGB 0..1) and whether it is water.</summary>
public readonly record struct HorizonSample(float Height, Vector3 Color, bool Water);

/// <summary>A horizon mesh in world metres: positions, normals, colours (rgb + 1 for water) and triangle indices.</summary>
public sealed class HorizonMesh
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector4> Colors { get; } = new();
    public List<int> Indices { get; } = new();
    public Vector2 Centre { get; init; }
}

/// <summary>
/// The world's surface seen from afar, sampled from the generator's fields rather than voxels (so it costs nothing to
/// stream): terrain height, the surface biome's map colour, and the sea over low ground and past the world's edge.
/// <see cref="Build"/> makes a polar mesh whose rings widen with distance, a continuous level of detail: fine next to
/// the streamed chunks, coarse at the horizon. Caves, structures and edits are not in it (they are close by, in voxels).
/// </summary>
public sealed class HorizonField
{
    private readonly WorldGenerator _gen;
    private readonly Vector3[] _biomeColors;
    private readonly Vector3 _sea = GrassKind.Rgb("#2a5f9e");
    private readonly Vector3 _deepSea = GrassKind.Rgb("#163a66");

    public HorizonField(WorldGenerator generator)
    {
        _gen = generator;
        var biomes = generator.Content.Biomes.All;
        _biomeColors = new Vector3[biomes.Count];
        for (int i = 0; i < biomes.Count; i++) _biomeColors[i] = GrassKind.Rgb(biomes[i].MapColor);
    }

    public float SeaLevel => _gen.Plan.Bands.SeaLevel * Units.CellSize;

    /// <summary>The surface at a point in world metres.</summary>
    public HorizonSample Sample(float x, float z)
    {
        float cx = x / Units.CellSize, cz = z / Units.CellSize;
        var size = _gen.Plan.Size;
        if (cx < 0 || cz < 0 || cx >= size.X || cz >= size.Z) return new HorizonSample(SeaLevel, _deepSea, true);
        float ground = _gen.Terrain.SurfaceHeight(cx, cz) * Units.CellSize;
        if (ground < SeaLevel)
        {
            float depth = Math.Clamp((SeaLevel - ground) / 12f, 0f, 1f);
            return new HorizonSample(SeaLevel, Vector3.Lerp(_sea, _deepSea, depth), true);
        }
        byte biome = _gen.Biomes.SurfaceBiome(cx, cz);
        var color = biome < _biomeColors.Length ? _biomeColors[biome] : new Vector3(0.3f, 0.5f, 0.25f);
        return new HorizonSample(ground, color, false);
    }

    /// <summary>
    /// A polar grid around <paramref name="centre"/> (metres, xz) from <paramref name="innerRadius"/> out to
    /// <paramref name="outerRadius"/>: <paramref name="segments"/> spokes, rings spaced so cells stay about square.
    /// </summary>
    public HorizonMesh Build(Vector2 centre, float innerRadius, float outerRadius, int segments = 192)
    {
        if (innerRadius <= 0 || outerRadius <= innerRadius || segments < 8) throw new ArgumentOutOfRangeException(nameof(outerRadius));
        var mesh = new HorizonMesh { Centre = centre };
        float growth = 1f + MathF.Tau / segments;
        var radii = new List<float>();
        for (float r = innerRadius; r < outerRadius * growth; r *= growth) radii.Add(MathF.Min(r, outerRadius));
        var heights = new float[radii.Count, segments];
        for (int ring = 0; ring < radii.Count; ring++)
            for (int s = 0; s < segments; s++)
            {
                float a = s * MathF.Tau / segments;
                float x = centre.X + MathF.Cos(a) * radii[ring], z = centre.Y + MathF.Sin(a) * radii[ring];
                var sample = Sample(x, z);
                heights[ring, s] = sample.Height;
                mesh.Positions.Add(new Vector3(x, sample.Height, z));
                mesh.Colors.Add(new Vector4(sample.Color, sample.Water ? 1f : 0f));
            }
        for (int ring = 0; ring < radii.Count; ring++)
            for (int s = 0; s < segments; s++)
                mesh.Normals.Add(NormalAt(heights, radii, ring, s, segments));
        for (int ring = 0; ring + 1 < radii.Count; ring++)
            for (int s = 0; s < segments; s++)
            {
                int a = ring * segments + s, b = ring * segments + (s + 1) % segments;
                int c = a + segments, d = b + segments;
                // Clockwise seen from above (Godot's front faces).
                mesh.Indices.AddRange(new[] { a, c, b, b, c, d });
            }
        return mesh;
    }

    private static Vector3 NormalAt(float[,] h, List<float> radii, int ring, int s, int segments)
    {
        int r0 = Math.Max(ring - 1, 0), r1 = Math.Min(ring + 1, radii.Count - 1);
        float dr = MathF.Max(radii[r1] - radii[r0], 1e-3f);
        float dhdr = (h[r1, s] - h[r0, s]) / dr;
        float arc = radii[ring] * MathF.Tau / segments * 2f;
        float dhda = (h[ring, (s + 1) % segments] - h[ring, (s + segments - 1) % segments]) / MathF.Max(arc, 1e-3f);
        float a = s * MathF.Tau / segments;
        var radial = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
        var tangent = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a));
        return Vector3.Normalize(Vector3.UnitY - radial * dhdr - tangent * dhda);
    }
}
