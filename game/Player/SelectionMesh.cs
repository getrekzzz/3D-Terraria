using Godot;
using Strata.Core.Meshing;

namespace Strata.Game.Player;

/// <summary>
/// Geometry of the block cursor, rebuilt every frame: thick camera-facing edges (drawn twice — solid where visible,
/// faint where terrain hides them, so the cube always reads), faces for the fill and crack overlay, and translucent
/// shape ghosts for placement.
/// </summary>
public sealed partial class SelectionMesh : Node3D
{
    private readonly ImmediateMesh _edges = new();
    private readonly ImmediateMesh _faces = new();
    private readonly ImmediateMesh _ghost = new();
    private ShaderMaterial _faceMaterial = null!;
    private bool _edgesOpen, _facesOpen, _ghostOpen;

    public Vector3 Eye { get; set; }

    public void Initialize()
    {
        TopLevel = true;
        var hidden = Unshaded(new Color(1, 1, 1, 0.38f), depthTest: false);
        hidden.RenderPriority = -1;
        var visible = Unshaded(Colors.White, depthTest: true);
        visible.RenderPriority = 1;
        Add(_edges, hidden);
        Add(_edges, visible);
        _faceMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/selection.gdshader") };
        Add(_faces, _faceMaterial);
        var ghost = Unshaded(Colors.White, depthTest: true);
        ghost.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        Add(_ghost, ghost);
    }

    public void Clear()
    {
        _edges.ClearSurfaces();
        _faces.ClearSurfaces();
        _ghost.ClearSurfaces();
        _edgesOpen = _facesOpen = _ghostOpen = false;
    }

    /// <summary>Finishes the surfaces begun this frame.</summary>
    public void Commit()
    {
        if (_edgesOpen) _edges.SurfaceEnd();
        if (_facesOpen) _faces.SurfaceEnd();
        if (_ghostOpen) _ghost.SurfaceEnd();
        _edgesOpen = _facesOpen = _ghostOpen = false;
    }

    /// <summary>The twelve edges of a box as ribbons about <paramref name="width"/> metres wide at 5 m.</summary>
    public void Box(Vector3 a, Vector3 b, Color color, float width = 0.042f)
    {
        var c = new[]
        {
            new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, a.Y, a.Z), new Vector3(b.X, a.Y, b.Z), new Vector3(a.X, a.Y, b.Z),
            new Vector3(a.X, b.Y, a.Z), new Vector3(b.X, b.Y, a.Z), new Vector3(b.X, b.Y, b.Z), new Vector3(a.X, b.Y, b.Z),
        };
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        if (!_edgesOpen)
        {
            _edges.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            _edgesOpen = true;
        }
        for (int i = 0; i < edges.Length; i += 2) Ribbon(c[edges[i]], c[edges[i + 1]], color, width);
    }

    private void Ribbon(Vector3 p, Vector3 q, Color color, float width)
    {
        var mid = (p + q) * 0.5f;
        float scale = Mathf.Clamp(Eye.DistanceTo(mid) / 5f, 0.4f, 3f);
        var along = (q - p).Normalized();
        var side = along.Cross(Eye - mid).Normalized() * width * 0.5f * scale;
        // Extend past the corners so neighbouring ribbons join without notches.
        p -= along * width * 0.5f * scale;
        q += along * width * 0.5f * scale;
        foreach (var v in new[] { p - side, p + side, q + side, p - side, q + side, q - side })
        {
            _edges.SurfaceSetColor(color);
            _edges.SurfaceAddVertex(v);
        }
    }

    /// <summary>The six faces of a box for the fill and crack overlay (see selection.gdshader).</summary>
    public void Faces(Vector3 a, Vector3 b, Color fill, float progress)
    {
        _faceMaterial.SetShaderParameter("fill_color", fill);
        _faceMaterial.SetShaderParameter("progress", progress);
        if (!_facesOpen)
        {
            _faces.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            _facesOpen = true;
        }
        const float e = 0.01f;
        a -= Vector3.One * e;
        b += Vector3.One * e;
        var size = b - a;
        Quad(a, new Vector3(size.X, 0, 0), new Vector3(0, size.Y, 0));                        // -Z
        Quad(new Vector3(b.X, a.Y, b.Z), new Vector3(-size.X, 0, 0), new Vector3(0, size.Y, 0)); // +Z
        Quad(new Vector3(a.X, a.Y, b.Z), new Vector3(0, 0, -size.Z), new Vector3(0, size.Y, 0)); // -X
        Quad(new Vector3(b.X, a.Y, a.Z), new Vector3(0, 0, size.Z), new Vector3(0, size.Y, 0));  // +X
        Quad(new Vector3(a.X, b.Y, a.Z), new Vector3(size.X, 0, 0), new Vector3(0, 0, size.Z));  // top
        Quad(new Vector3(a.X, a.Y, b.Z), new Vector3(size.X, 0, 0), new Vector3(0, 0, -size.Z)); // bottom
    }

    private void Quad(Vector3 origin, Vector3 u, Vector3 v)
    {
        float lu = u.Length(), lv = v.Length();
        foreach (var (s, t) in new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 0f), (1f, 1f), (0f, 1f) })
        {
            _faces.SurfaceSetUV(new Vector2(s, t));
            _faces.SurfaceSetUV2(new Vector2(s * lu, t * lv));
            _faces.SurfaceAddVertex(origin + u * s + v * t);
        }
    }

    /// <summary>A translucent ghost of a crisp cell shape (or full cube) at a cell's minimum corner.</summary>
    public void Ghost(Vector3 origin, byte shapeCode, float cellSize, Color color)
    {
        if (!_ghostOpen)
        {
            _ghost.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            _ghostOpen = true;
        }
        foreach (var poly in ShapeGeometry.Get(shapeCode))
            for (int i = 1; i + 1 < poly.Points.Length; i++)
                foreach (var p in new[] { poly.Points[0], poly.Points[i], poly.Points[i + 1] })
                {
                    _ghost.SurfaceSetColor(color);
                    _ghost.SurfaceAddVertex(origin + new Vector3(p.X, p.Y, p.Z) * cellSize);
                }
    }

    private void Add(ImmediateMesh mesh, Material material) =>
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

    private static StandardMaterial3D Unshaded(Color albedo, bool depthTest) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        AlbedoColor = albedo,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = !depthTest,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
