using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// Cracks on every damaged block near the camera, not only the one under the cursor: up to 64 box decals
/// (<c>block_crack.gdshader</c>) in one MultiMesh, staged by damage (any, 50%, 75%, 90%) and fading out over the last
/// second before the block forgets its damage. Rebuilt when the damage tracker changes, and four times a second while
/// any crack is fading.
/// </summary>
public sealed partial class BlockCracks : Node3D
{
    private const int MaxCracks = 64;
    private const float Range = 24f, BoxScale = 1.04f, Refresh = 0.25f;
    private const int FadeTicks = 60;

    private GameWorld _game = null!;
    private MultiMesh _mm = null!;
    private int _version = -1;
    private float _timer;
    private bool _fading;

    public static BlockCracks? Instance { get; private set; }

    /// <summary>Crack decals drawn (probe <c>effects.cracks</c>).</summary>
    public int Count => _mm.VisibleInstanceCount;

    public void Initialize(GameWorld game)
    {
        Name = "BlockCracks";
        _game = game;
        Instance = this;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/block_crack.gdshader"), RenderPriority = 1 };
        material.SetShaderParameter("cracks", GD.Load<Texture2D>("res://assets/textures/fx/cracks.png"));
        material.SetShaderParameter("tile_size", Strata.Core.Gameplay.Building.BlockGrid.Size * Units.CellSize);
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            InstanceCount = MaxCracks,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "Decals" });
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.cracks");
        var tracker = _game.Editor.Damage;
        _timer -= (float)delta;
        if (tracker.Version == _version && !(_fading && _timer <= 0f)) return;
        _version = tracker.Version;
        _timer = Refresh;
        Rebuild(tracker);
    }

    private void Rebuild(BlockDamageTracker tracker)
    {
        var eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        long tick = _game.Session.Clock.Tick;
        float size = Units.CellSize * BoxScale;
        int n = 0;
        foreach (var (cell, (damage, hitTick)) in tracker)
        {
            if (n >= MaxCracks) break;
            long left = tracker.ForgetAfterTicks - (tick - hitTick);
            if (left <= 0) continue;
            var center = (new Vector3(cell.X, cell.Y, cell.Z) + Vector3.One * 0.5f) * Units.CellSize;
            if (center.DistanceSquaredTo(eye) > Range * Range) continue;
            float fraction = damage / MiningRules.TileHealth;
            int stage = fraction >= 0.9f ? 3 : fraction >= 0.75f ? 2 : fraction >= 0.5f ? 1 : 0;
            float fade = Mathf.Clamp(left / (float)FadeTicks, 0f, 1f);
            _mm.SetInstanceTransform(n, new Transform3D(Basis.FromScale(Vector3.One * size), center));
            _mm.SetInstanceCustomData(n, new Color(stage / 3f, fade, 0f, 0f));
            n++;
        }
        // While any crack shows, the timer keeps rebuilding so fades start and end on time.
        _fading = n > 0;
        _mm.VisibleInstanceCount = n;
    }
}
