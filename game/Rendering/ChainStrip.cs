using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// A chain drawn into an open triangle surface of an ImmediateMesh: alternating flat diamond links a few centimetres
/// long from one point to another (grappling hooks, flails). Nothing is allocated per link.
/// </summary>
public static class ChainStrip
{
    /// <summary>Most links drawn for one chain.</summary>
    public const int MaxLinks = 400;

    /// <summary>Adds the links from <paramref name="from"/> to <paramref name="to"/> in <paramref name="color"/>; returns the chain's side and up axes.</summary>
    public static (Vector3 Side, Vector3 Up) Links(ImmediateMesh mesh, Vector3 from, Vector3 to, Color color, float link = 0.09f, float width = 0.022f)
    {
        var along = to - from;
        float length = along.Length();
        var dir = length > 1e-4f ? along / length : Vector3.Forward;
        var side = dir.Cross(Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var up = side.Cross(dir).Normalized();
        int links = Mathf.Min(MaxLinks, (int)(length / link));
        mesh.SurfaceSetColor(color);
        for (int i = 0; i < links; i++)
        {
            var c = from + dir * (i + 0.5f) * link;
            var w = (i % 2 == 0 ? side : up) * width;
            Quad(mesh, c - dir * link * 0.5f, c + w, c + dir * link * 0.5f, c - w);
        }
        return (side, up);
    }

    /// <summary>A flat quad as two triangles in the colour set last.</summary>
    public static void Quad(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
        mesh.SurfaceAddVertex(c);
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(c);
        mesh.SurfaceAddVertex(d);
    }
}
