using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Game.Audio;
using Strata.Game.Player;

namespace Strata.Game.Entities;

/// <summary>
/// Boss presentation in the world: the camera jolts when a boss arrives or dies, the soundtrack falls silent for a few
/// seconds after a win, a slam shakes the camera by distance and throws a ring of dust in the colour of the ground it
/// hit (8 pooled emitters), and brain cues (<see cref="NpcSystem.Cued"/>, ids are sound ids) play where the creature is.
/// </summary>
public sealed partial class BossEffects : Node3D
{
    public const float SpawnKick = 0.45f;
    public const float DefeatKick = 0.7f;
    public const double DefeatSilence = 3.0;
    private const float ImpactReach = 40f;
    private const int RingCount = 8;

    private readonly CpuParticles3D[] _rings = new CpuParticles3D[RingCount];
    private int _nextRing;
    private GameWorld _game = null!;

    public void Initialize(GameWorld game)
    {
        Name = "BossEffects";
        _game = game;
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex };
        var mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 6, Rings = 3, Material = material };
        for (int i = 0; i < RingCount; i++)
        {
            _rings[i] = new CpuParticles3D
            {
                Emitting = false,
                OneShot = true,
                Amount = 40,
                Lifetime = 0.9,
                Explosiveness = 0.95f,
                Direction = new Vector3(1, 0.15f, 0),
                Spread = 180f,
                Flatness = 0.9f, // a ring along the ground, not a ball
                InitialVelocityMin = 3f,
                InitialVelocityMax = 6f,
                DampingMin = 3f,
                DampingMax = 5f,
                Gravity = new Vector3(0, -2f, 0),
                ScaleAmountMin = 0.7f,
                ScaleAmountMax = 1.6f,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
                EmissionRingAxis = Vector3.Up,
                EmissionRingRadius = 0.8f,
                EmissionRingInnerRadius = 0.5f,
                EmissionRingHeight = 0.1f,
                Mesh = mesh,
            };
            AddChild(_rings[i]);
        }
        game.Npcs.BossSpawned += _ => CameraShake.Kick(SpawnKick);
        game.Npcs.BossDefeated += _ =>
        {
            CameraShake.Kick(DefeatKick);
            MusicDirector.Instance?.Silence(DefeatSilence);
        };
        game.Npcs.Impacted += OnImpact;
        game.Npcs.Cued += (npc, id) => Sfx.PlayAt(id, ToGodot(npc.Center), npc.IsBoss || npc.IsPart ? 3f : 0f);
    }

    private void OnImpact(System.Numerics.Vector3 at, float strength)
    {
        var point = ToGodot(at);
        float distance = GetViewport().GetCamera3D() is { } camera ? camera.GlobalPosition.DistanceTo(point) : 0f;
        CameraShake.Kick(strength * Mathf.Clamp(1f - distance / ImpactReach, 0f, 1f));
        Sfx.PlayAt("boss_slam", point, Mathf.Lerp(-6f, 3f, Mathf.Clamp(strength, 0f, 1f)));
        var ring = _rings[_nextRing];
        _nextRing = (_nextRing + 1) % RingCount;
        ring.GlobalPosition = point;
        ring.Color = GroundColor(at);
        ring.InitialVelocityMax = 3f + 5f * strength;
        ring.Restart();
        ring.Emitting = true;
    }

    /// <summary>The colour of the first block at or below the point (a little darker, like dust), or earth brown.</summary>
    private Color GroundColor(System.Numerics.Vector3 at)
    {
        var session = _game.Session;
        var cell = new Int3((int)System.MathF.Floor(at.X / Units.CellSize), (int)System.MathF.Floor(at.Y / Units.CellSize), (int)System.MathF.Floor(at.Z / Units.CellSize));
        for (int dy = 0; dy < 4; dy++)
        {
            ushort id = session.World.GetBlock(new Int3(cell.X, cell.Y - dy, cell.Z));
            if (id == 0 || id == VoxelWorld.Unknown || id >= session.Content.Blocks.Count) continue;
            return Color.FromHtml(session.Content.Blocks[id].Color).Darkened(0.15f);
        }
        return new Color(0.45f, 0.36f, 0.27f);
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
