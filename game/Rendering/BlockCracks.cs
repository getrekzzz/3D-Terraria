using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// Cracks on every damaged block near the camera, not only the one under the cursor: up to 64 box decals
/// (<c>block_crack.gdshader</c>) in one MultiMesh, staged by damage (any, 50%, 75%, 90%) and fading out over the last
/// second before the block forgets its damage (the newest hits first past the cap). Rebuilt when the damage tracker
/// changes, and four times a second while any damage is on record.
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
    private readonly (Vector3 Center, float Damage, long Tick)[] _cells = new (Vector3, float, long)[512];
    private readonly long[] _order = new long[512];
    private readonly int[] _index = new int[512];

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
        // Candidates in range, newest hit first when there are more than the cap (the block being mined always shows).
        int n = 0;
        foreach (var (cell, (damage, hitTick)) in tracker)
        {
            if (n >= _cells.Length) break;
            if (tick - hitTick >= tracker.ForgetAfterTicks) continue;
            var center = (new Vector3(cell.X, cell.Y, cell.Z) + Vector3.One * 0.5f) * Units.CellSize;
            if (center.DistanceSquaredTo(eye) > Range * Range) continue;
            _cells[n] = (center, damage, hitTick);
            _order[n] = -hitTick;
            _index[n] = n;
            n++;
        }
        if (n > MaxCracks) System.Array.Sort(_order, _index, 0, n);
        int shown = Mathf.Min(n, MaxCracks);
        float size = Units.CellSize * BoxScale;
        for (int i = 0; i < shown; i++)
        {
            var (center, damage, hitTick) = _cells[_index[i]];
            float fraction = damage / MiningRules.TileHealth;
            int stage = fraction >= 0.9f ? 3 : fraction >= 0.75f ? 2 : fraction >= 0.5f ? 1 : 0;
            long left = tracker.ForgetAfterTicks - (tick - hitTick);
            float fade = Mathf.Clamp(left / (float)FadeTicks, 0f, 1f);
            _mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * size), center));
            _mm.SetInstanceCustomData(i, new Color(stage / 3f, fade, 0f, 0f));
        }
        // While any damage is on record the timer keeps rebuilding: fades run on time and far cracks appear as you
        // walk up to them.
        _fading = tracker.Count > 0;
        _mm.VisibleInstanceCount = shown;
    }
}
