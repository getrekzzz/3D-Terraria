using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Muzzle flashes (P3-10): a pool of four additive star-shaped billboards, each shown for a few hundredths of a
/// second at a gun's muzzle, shrinking as it fades. No per-shot allocation.
/// </summary>
public sealed partial class MuzzleFlashes : Node3D
{
    public const int Pool = 4;
    private const float Life = 0.05f;

    private readonly MeshInstance3D[] _quads = new MeshInstance3D[Pool];
    private readonly StandardMaterial3D[] _materials = new StandardMaterial3D[Pool];
    private readonly float[] _left = new float[Pool];
    private readonly float[] _size = new float[Pool];
    private int _next;

    /// <summary>Flashes shown since start (probe <c>fx.muzzle_flashes</c>).</summary>
    public int Shown { get; private set; }

    public override void _Ready()
    {
        Name = "MuzzleFlashes";
        var texture = StarTexture();
        for (int i = 0; i < Pool; i++)
        {
            _materials[i] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoTexture = texture, NoDepthTest = false, DisableReceiveShadows = true,
            };
            _quads[i] = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = _materials[i], Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_quads[i]);
        }
    }

    /// <summary>Shows a flash <paramref name="size"/> metres across at <paramref name="at"/>, tinted toward <paramref name="color"/>.</summary>
    public void Flash(Vector3 at, Color color, float size)
    {
        int i = _next;
        _next = (_next + 1) % Pool;
        _left[i] = Life;
        _size[i] = size;
        _materials[i].AlbedoColor = new Color(1f, 0.92f, 0.7f).Lerp(color, 0.35f);
        var quad = _quads[i];
        quad.GlobalPosition = at;
        quad.Rotation = new Vector3(0, 0, GD.Randf() * Mathf.Tau); // billboard spin: each flash a different star
        quad.Scale = Vector3.One * size;
        quad.Visible = true;
        Shown++;
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < Pool; i++)
        {
            if (_left[i] <= 0f) continue;
            _left[i] -= (float)delta;
            var quad = _quads[i];
            if (_left[i] <= 0f)
            {
                quad.Visible = false;
                continue;
            }
            float k = _left[i] / Life;
            quad.Scale = Vector3.One * _size[i] * (0.6f + 0.4f * k);
        }
    }

    /// <summary>A soft four-pointed star: a hot core with spikes, fading to clear at the edge.</summary>
    private static ImageTexture StarTexture()
    {
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Clamp(1f - r * 2.2f, 0f, 1f);
                float spikes = Mathf.Clamp(1f - Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)) * 9f, 0f, 1f) * Mathf.Clamp(1f - r, 0f, 1f);
                float a = Mathf.Clamp(core * core + spikes * 0.8f, 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return ImageTexture.CreateFromImage(image);
    }
}
