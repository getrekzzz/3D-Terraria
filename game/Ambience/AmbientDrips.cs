using Godot;
using Strata.Core.Common;
using Strata.Game.Audio;
using Strata.Game.World;

namespace Strata.Game.Ambience;

/// <summary>
/// Water dripping from cave ceilings near the camera: a few sources are found on real ceilings (a solid cell with open
/// air under it and a floor below), each grows a drop, lets it fall and plinks where it lands (a quiet positional
/// sound that the cave reverb carries). Drawn as one small multimesh; sources are re-picked as the camera moves.
/// </summary>
public sealed partial class AmbientDrips : Node3D
{
    private const int MaxDrips = 24;
    private const float Gravity = 9.8f;

    private WorldRuntime _runtime = null!;
    private MultiMeshInstance3D _mesh = null!;
    private readonly Drip[] _drips = new Drip[MaxDrips];
    private readonly RandomNumberGenerator _rng = new();
    private float _search;
    private float _soundGap;

    private struct Drip
    {
        public bool Live;
        public Vector3 From;
        public float FloorY;
        public float Wait; // seconds until the drop lets go
        public float Fall; // seconds falling (−1 hanging)
    }

    /// <summary>How many drip sources should be active here (0 outside caves).</summary>
    public int Wanted { get; set; }

    public int Active { get; private set; }

    public void Initialize(WorldRuntime runtime)
    {
        Name = "Drips";
        _runtime = runtime;
        _rng.Randomize();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.65f, 0.75f, 0.85f, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = MaxDrips,
            VisibleInstanceCount = 0,
            Mesh = new SphereMesh { Radius = 0.018f, Height = 0.06f, RadialSegments = 6, Rings = 3, Material = mat },
        };
        _mesh = new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
        AddChild(_mesh);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var eye = camera.GlobalPosition;
        _soundGap -= dt;
        _search -= dt;
        int live = 0;
        for (int i = 0; i < MaxDrips; i++) if (_drips[i].Live) live++;
        if (_search <= 0f && live < Mathf.Min(Wanted, MaxDrips))
        {
            _search = 0.3f;
            TryAdd(eye);
        }
        var mm = _mesh.Multimesh;
        int shown = 0;
        for (int i = 0; i < MaxDrips; i++)
        {
            ref var d = ref _drips[i];
            if (!d.Live) continue;
            if (d.From.DistanceSquaredTo(eye) > 24f * 24f || i >= Wanted)
            {
                d.Live = false;
                continue;
            }
            Vector3 at;
            if (d.Fall < 0f)
            {
                d.Wait -= dt;
                if (d.Wait <= 0f) d.Fall = 0f;
                at = d.From;
            }
            else
            {
                d.Fall += dt;
                at = d.From + Vector3.Down * (0.5f * Gravity * d.Fall * d.Fall);
                if (at.Y <= d.FloorY)
                {
                    Landed(new Vector3(at.X, d.FloorY, at.Z), eye);
                    d.Fall = -1f;
                    d.Wait = _rng.RandfRange(1.5f, 6f);
                    continue;
                }
            }
            mm.SetInstanceTransform(shown++, new Transform3D(Basis.Identity, at));
        }
        mm.VisibleInstanceCount = shown;
        Active = live;
    }

    private void Landed(Vector3 at, Vector3 eye)
    {
        if (_soundGap > 0f || at.DistanceSquaredTo(eye) > 14f * 14f) return;
        _soundGap = 0.35f;
        Sfx.PlayAt("cave_drip", at, -12f, 0.15f);
    }

    /// <summary>Looks for a ceiling over open air with a floor below in a random column near the camera.</summary>
    private void TryAdd(Vector3 eye)
    {
        var world = _runtime.Session.World;
        int cx = Mathf.FloorToInt(eye.X / Units.CellSize) + _rng.RandiRange(-16, 16);
        int cz = Mathf.FloorToInt(eye.Z / Units.CellSize) + _rng.RandiRange(-16, 16);
        int cy = Mathf.FloorToInt(eye.Y / Units.CellSize);
        if (world.IsSolid(cx, cy, cz)) return;
        int ceiling = int.MinValue;
        for (int y = cy + 1; y <= cy + 14; y++)
            if (world.IsSolid(cx, y, cz))
            {
                ceiling = y;
                break;
            }
        if (ceiling == int.MinValue) return;
        int floor = int.MinValue;
        for (int y = ceiling - 2; y >= cy - 16; y--)
            if (world.IsSolid(cx, y, cz))
            {
                floor = y + 1;
                break;
            }
        if (floor == int.MinValue || ceiling - floor < 3) return;
        for (int i = 0; i < MaxDrips; i++)
        {
            if (_drips[i].Live) continue;
            float jitter = _rng.RandfRange(0.15f, 0.35f);
            _drips[i] = new Drip
            {
                Live = true,
                From = new Vector3((cx + jitter * 2f) * Units.CellSize, ceiling * Units.CellSize - 0.03f, (cz + 0.5f) * Units.CellSize),
                FloorY = floor * Units.CellSize,
                Wait = _rng.RandfRange(0.3f, 3f),
                Fall = -1f,
            };
            return;
        }
    }
}
