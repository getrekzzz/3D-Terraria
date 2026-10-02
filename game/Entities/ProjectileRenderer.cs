using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Projectiles: arrows and thrown items use their item model aligned with the flight direction (boomerangs spin);
/// minions with a creature model face where they fly; magic bolts are glowing orbs. Glowing shots near the camera
/// borrow one of a few pooled lights (ProjectileRenderer.Lights.cs). Fishing bobbers bob on the water, dip when a fish
/// bites, and hang from a line to the rod tip. Pets: ProjectileRenderer.Pets.cs. Materials and meshes are shared and
/// finished nodes go back to a pool per projectile type when their shot ends (P1b-15). The local player's shots are
/// drawn leaving the held item's muzzle: the view starts there and eases onto the simulated path (P3-10).
/// </summary>
public sealed partial class ProjectileRenderer : Node3D
{
    private readonly Dictionary<long, Node3D> _views = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private readonly ImmediateMesh _lines = new();
    private float _clock;

    public void Initialize(GameWorld game, PropLibrary props)
    {
        _game = game;
        _props = props;
        Name = "Projectiles";
        game.Projectiles.Removed += p =>
        {
            if (_views.Remove(p.Id, out var node)) Release(p, node);
            _petAnims.Remove(p.Id);
            _awaiting.Remove(p.Id);
            _offsets.Remove(p.Id);
            EndTrail(p.Id);
        };
        game.Projectiles.Spawned += StartAtMuzzle;
        AddChild(new MeshInstance3D
        {
            Mesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.92f, 0.92f, 0.9f) },
        });
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.projectiles");
        _clock += (float)delta;
        _lines.ClearSurfaces();
        bool anyLine = false;
        _retryNow = _awaiting.Count > 0 && _clock - _retryClock >= 0.25f;
        if (_retryNow) _retryClock = _clock;
        foreach (var p in _game.Projectiles.All)
        {
            if (!_views.TryGetValue(p.Id, out var node) || (_awaiting.Contains(p.Id) && RetryModel(p, ref node)))
            {
                node ??= Create(p);
                if (node.GetParent() == null) AddChild(node);
                _views[p.Id] = node;
            }
            var pos = new Vector3(p.Position.X, p.Position.Y, p.Position.Z) + MuzzleOffset(p.Id, (float)delta);
            var vel = new Vector3(p.Velocity.X, p.Velocity.Y, p.Velocity.Z);
            if (p.Def.Bobber)
            {
                if (p.Floating) pos.Y += p.Biting ? -0.1f + 0.05f * Mathf.Sin(_clock * 30f) : 0.02f * Mathf.Sin(_clock * 3f);
                node.Position = pos;
                if (p.Owner is { } owner)
                {
                    if (!anyLine) _lines.SurfaceBegin(Mesh.PrimitiveType.Lines);
                    anyLine = true;
                    var aim = new Vector3(owner.Aim.X, 0.35f, owner.Aim.Z).Normalized();
                    var tip = new Vector3(owner.Position.X, owner.Position.Y + 1.3f, owner.Position.Z) + aim * 1.2f;
                    _lines.SurfaceAddVertex(tip);
                    _lines.SurfaceAddVertex(pos + Vector3.Up * 0.06f);
                }
                continue;
            }
            node.Position = pos;
            if (p.PetSlot != null)
            {
                AnimatePet(p, node, vel);
                continue;
            }
            if (p.Def.Minion != null)
            {
                // Creatures stay upright and turn to face their flight.
                if (new Vector2(vel.X, vel.Z).LengthSquared() > 0.04f) node.Rotation = new Vector3(0, Mathf.Atan2(vel.X, vel.Z), 0);
                continue;
            }
            if (vel.LengthSquared() > 0.01f)
            {
                // Models point along +Y: rotate +Y onto the velocity.
                var dir = vel.Normalized();
                var axis = Vector3.Up.Cross(dir);
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(dir), -1f, 1f));
                var along = axis.LengthSquared() > 1e-6f ? new Basis(axis.Normalized(), angle) : (dir.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity);
                node.Basis = Spun(p, along);
            }
            Sample(p, pos); // its trail (ProjectileRenderer.Trails)
            QueueString(p, pos); // a yoyo's string (ProjectileRenderer.Tethers)
        }
        if (anyLine) _lines.SurfaceEnd();
        DrawTrails((float)delta);
        DrawStrings();
        PlaceLights(); // the pooled lights go to the glowing shots nearest the camera
    }

    /// <summary>A view for a new projectile: a pooled one of its type when there is one, else a fresh one.</summary>
    private Node3D Create(Projectile p)
    {
        if (Poolable(p) && _pool.TryGetValue(p.Def.Id, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            Pooled--;
            reused.Transform = Transform3D.Identity;
            reused.Visible = true;
            return reused;
        }
        var node = Build(p);
        if (Poolable(p) && !_awaiting.Contains(p.Id)) _pooledType[node] = p.Def.Id;
        return node;
    }
}
