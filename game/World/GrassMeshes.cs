using System;
using Godot;

namespace Strata.Game.World;

/// <summary>
/// Builds the grass clump and flower meshes drawn by <see cref="GrassRenderer"/>. Horizontal sizes are in metres;
/// the vertical axis is normalised (1 = clump height; the instance transform scales it). UV.x runs across a blade,
/// UV.y from root (0) to tip (1) and drives the wind bend; UV2.x is a per-blade random number, UV2.y marks petals.
/// </summary>
public static class GrassMeshes
{
    /// <summary>Full detail for nearby grass: many curved blades with several segments each.</summary>
    public static ArrayMesh Clump(int blades, int segments, int seed)
    {
        var b = new Builder();
        var rng = new Random(seed);
        for (int i = 0; i < blades; i++)
        {
            // Blades spread around the clump centre and lean outward, the outer ones lower and more bent.
            float r = 0.13f * MathF.Sqrt((i + 0.5f) / blades) + 0.02f * (float)rng.NextDouble();
            float a = i * 2.39996f + (float)rng.NextDouble() * 0.6f;
            var root = new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float outward = r / 0.15f;
            float lean = 0.03f + 0.1f * outward + 0.05f * (float)rng.NextDouble();
            float leanAngle = a + ((float)rng.NextDouble() - 0.5f) * 1.2f;
            var leanDir = new Vector3(MathF.Cos(leanAngle), 0f, MathF.Sin(leanAngle));
            float height = 1f - 0.35f * outward * (float)rng.NextDouble();
            float width = 0.009f + 0.007f * (float)rng.NextDouble();
            float twist = ((float)rng.NextDouble() - 0.5f) * 1.2f;
            b.Blade(root, leanDir, lean, height, width, twist, segments, (float)rng.NextDouble());
        }
        return b.Commit();
    }

    /// <summary>A flower: a thin stem with a ring of petals around a small centre, facing up.</summary>
    public static ArrayMesh Flower(int seed)
    {
        var b = new Builder();
        var rng = new Random(seed);
        var lean = new Vector3(0.02f, 0f, 0.01f);
        b.Blade(Vector3.Zero, lean.Normalized(), lean.Length(), 1f, 0.004f, 0f, 3, 0.5f);
        var top = new Vector3(0.022f, 1f, 0.011f);
        const int petals = 6;
        for (int i = 0; i < petals; i++)
        {
            float a = i * MathF.Tau / petals + (float)rng.NextDouble() * 0.2f;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            var side = new Vector3(-dir.Z, 0f, dir.X);
            float len = 0.028f + 0.006f * (float)rng.NextDouble();
            // Petal quad: narrow at the centre, widest two thirds out, lifted toward the tip (a shallow cup).
            var p0 = top + side * 0.004f;
            var p1 = top - side * 0.004f;
            var mid = top + dir * len * 0.65f + new Vector3(0f, 0.02f, 0f);
            var p2 = mid - side * 0.011f;
            var p3 = mid + side * 0.011f;
            var tip = top + dir * len + new Vector3(0f, 0.045f, 0f);
            var n = new Vector3(0f, 1f, 0f);
            int i0 = b.Vertex(p0, n, new Vector2(0f, 1f), new Vector2(0.05f, 1f));
            int i1 = b.Vertex(p1, n, new Vector2(1f, 1f), new Vector2(0.05f, 1f));
            int i2 = b.Vertex(p2, n, new Vector2(1f, 1f), new Vector2(0.65f, 1f));
            int i3 = b.Vertex(p3, n, new Vector2(0f, 1f), new Vector2(0.65f, 1f));
            int i4 = b.Vertex(tip, n, new Vector2(0.5f, 1f), new Vector2(1f, 1f));
            b.Triangle(i0, i1, i2);
            b.Triangle(i0, i2, i3);
            b.Triangle(i3, i2, i4);
        }
        return b.Commit();
    }

    private sealed class Builder
    {
        private readonly System.Collections.Generic.List<Vector3> _vertices = new();
        private readonly System.Collections.Generic.List<Vector3> _normals = new();
        private readonly System.Collections.Generic.List<Vector2> _uv = new();
        private readonly System.Collections.Generic.List<Vector2> _uv2 = new();
        private readonly System.Collections.Generic.List<int> _indices = new();

        public int Vertex(Vector3 p, Vector3 n, Vector2 uv, Vector2 uv2)
        {
            _vertices.Add(p);
            _normals.Add(n);
            _uv.Add(uv);
            _uv2.Add(uv2);
            return _vertices.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        /// <summary>A tapered blade curving from its root toward the lean direction, twisting slightly.</summary>
        public void Blade(Vector3 root, Vector3 leanDir, float lean, float height, float width, float twist, int segments, float random)
        {
            int first = _vertices.Count;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                var along = root + leanDir * (lean * t * t) + new Vector3(0f, height * t * (1f - 0.25f * lean * t), 0f);
                float yaw = MathF.Atan2(leanDir.Z, leanDir.X) + MathF.PI / 2f + twist * t;
                var side = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
                var tangent = (leanDir * (2f * lean * t) + new Vector3(0f, height, 0f)).Normalized();
                var normal = side.Cross(tangent).Normalized();
                float w = width * (1f - MathF.Pow(t, 1.6f));
                var uv2 = new Vector2(random, 0f);
                if (s == segments)
                {
                    Vertex(along, normal, new Vector2(0.5f, t), uv2);
                    break;
                }
                Vertex(along - side * w, normal, new Vector2(0f, t), uv2);
                Vertex(along + side * w, normal, new Vector2(1f, t), uv2);
            }
            for (int s = 0; s < segments - 1; s++)
            {
                int a = first + s * 2;
                Triangle(a, a + 1, a + 3);
                Triangle(a, a + 3, a + 2);
            }
            int last = first + (segments - 1) * 2;
            Triangle(last, last + 1, last + 2);
        }

        public ArrayMesh Commit()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
