using System.Threading.Tasks;
using Godot;
using Strata.Core.Common;
using Strata.Core.Presentation;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Draws the far view: a <see cref="HorizonField"/> mesh around the focus, out to the preset's horizon distance,
/// rebuilt on a worker thread whenever the focus drifts from its centre. The part over the loaded chunk box is
/// discarded in the shader, so real terrain takes over seamlessly near the player.
/// </summary>
public sealed partial class HorizonRenderer : Node3D
{
    private const float RebuildDistance = 48f;

    private HorizonField _field = null!;
    private GraphicsSettings _graphics = null!;
    private MeshInstance3D _instance = null!;
    private ShaderMaterial _material = null!;
    private Task<HorizonMesh>? _pending;
    private System.Numerics.Vector2 _centre = new(float.NaN, float.NaN);
    private float _builtDistance;

    public int Vertices { get; private set; }

    public void Initialize(HorizonField field, GraphicsSettings graphics)
    {
        _field = field;
        _graphics = graphics;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/horizon.gdshader") };
        _instance = new MeshInstance3D { Name = "HorizonMesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _material };
        AddChild(_instance);
    }

    /// <summary>Keeps the mesh centred near the focus and tells the shader which box the chunks cover.</summary>
    public void Update(Vector3 focus, Int3 focusChunk, int meshRadius, int meshDown, int meshUp)
    {
        float distance = _graphics.Preset.HorizonDistance;
        _instance.Visible = distance > 0f;
        if (distance <= 0f) return;
        float chunk = ChunkMath.Size * Units.CellSize;
        var min = new Vector2(focusChunk.X - meshRadius, focusChunk.Z - meshRadius) * chunk;
        var max = new Vector2(focusChunk.X + meshRadius + 1, focusChunk.Z + meshRadius + 1) * chunk;
        _material.SetShaderParameter("streamed_box", new Vector4(min.X, min.Y, max.X, max.Y));
        _material.SetShaderParameter("streamed_height", new Vector2((focusChunk.Y - meshDown) * chunk, (focusChunk.Y + meshUp + 1) * chunk));

        if (_pending is { IsCompleted: true } done)
        {
            _pending = null;
            if (done.IsCompletedSuccessfully) Upload(done.Result);
            else GD.PushError($"horizon build failed: {done.Exception?.GetBaseException().Message}");
        }
        var here = new System.Numerics.Vector2(focus.X, focus.Z);
        bool far = float.IsNaN(_centre.X) || System.Numerics.Vector2.Distance(here, _centre) > RebuildDistance;
        if (_pending == null && (far || distance != _builtDistance))
        {
            // Snap the centre so small movements do not reshape the distance.
            var centre = new System.Numerics.Vector2(System.MathF.Round(here.X / 16f) * 16f, System.MathF.Round(here.Y / 16f) * 16f);
            float inner = System.MathF.Max(8f, meshRadius * chunk * 0.5f);
            _centre = centre;
            _builtDistance = distance;
            var field = _field;
            _pending = Task.Run(() => field.Build(centre, inner, distance));
        }
    }

    private void Upload(HorizonMesh mesh)
    {
        int n = mesh.Positions.Count;
        var vertices = new Vector3[n];
        var normals = new Vector3[n];
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            var p = mesh.Positions[i];
            var q = mesh.Normals[i];
            var c = mesh.Colors[i];
            vertices[i] = new Vector3(p.X, p.Y, p.Z);
            normals[i] = new Vector3(q.X, q.Y, q.Z);
            colors[i] = new Color(c.X, c.Y, c.Z, c.W);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = mesh.Indices.ToArray();
        var array = new ArrayMesh();
        array.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _instance.Mesh = array;
        Vertices = n;
    }
}
