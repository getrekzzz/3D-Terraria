using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Projectiles: arrows and thrown items use their item model aligned with the flight direction (boomerangs spin);
/// minions with a creature model face where they fly; magic bolts are glowing orbs with a dynamic light (the plan
/// reserves dynamic lights for moving sources like these). Fishing bobbers bob on the water, dip when a fish bites,
/// and hang from a line to the rod tip.
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
            if (_views.Remove(p.Id, out var node)) node.QueueFree();
        };
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
        foreach (var p in _game.Projectiles.All)
        {
            if (!_views.TryGetValue(p.Id, out var node))
            {
                node = Create(p);
                AddChild(node);
                _views[p.Id] = node;
            }
            var pos = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
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
                node.Basis = axis.LengthSquared() > 1e-6f ? new Basis(axis.Normalized(), angle) : (dir.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity);
                if (p.Def.Returns > 0 || p.Def.Fuse) node.Basis *= new Basis(Vector3.Right, _clock * (p.Def.Fuse ? 6f : 22f)); // tumbling throws
            }
        }
        if (anyLine) _lines.SurfaceEnd();
    }

    private Node3D Create(Projectile p)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.projectiles.create");
        var root = new Node3D();
        var color = Color.FromHtml(p.Def.Color);
        var content = _game.Session.Content;
        if (p.Def.Visual == "creature" && CreatureVisual.Create($"assets/models/creatures/{p.Def.Model ?? p.Def.Id}.glb", _props) is { } creature)
        {
            creature.Scale = Vector3.One * p.Def.Scale;
            root.AddChild(creature);
            creature.SetTint(color);
            creature.Play("idle");
            AddLight(root, p);
            return root;
        }
        Mesh? mesh = null;
        if (p.Def.Visual == "item" && content.Items.TryGet(p.Def.Model ?? p.Def.Id, out var thrown) && AssetPaths.ItemModel(thrown, content) is { } itemPath)
            mesh = _props.Model(itemPath, multimesh: false);
        else if (p.Def.Model != null)
            mesh = _props.Model(AssetPaths.ProjectileModel(p.Def), multimesh: false);
        else if (p.Def.Visual == "arrow" && content.Items.TryGet(p.Def.Id, out var ammo) && AssetPaths.ItemModel(ammo, content) is { } path)
            mesh = _props.Model(path, multimesh: false); // ammo projectiles reuse their ammo item's model
        if (mesh != null)
        {
            root.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, -0.1f, 0), Scale = Vector3.One * p.Def.Scale });
        }
        else if (p.Def.Visual == "bullet")
        {
            // A short bright streak along the flight direction.
            root.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.025f, Height = 0.45f, RadialSegments = 6, Rings = 1 },
                MaterialOverride = Glow(color, 4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        else
        {
            float r = p.Def.Bobber ? 0.07f : p.Def.Radius * 0.25f;
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 10, Rings = 6 },
                MaterialOverride = p.Def.Bobber ? new StandardMaterial3D { AlbedoColor = color, Roughness = 0.4f } : Glow(color, 3f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        AddLight(root, p);
        return root;
    }

    private static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = energy, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static void AddLight(Node3D root, Projectile p)
    {
        if (p.Def.Light is { Length: 3 } l)
            root.AddChild(new OmniLight3D { LightColor = new Color(l[0] / 15f, l[1] / 15f, l[2] / 15f), OmniRange = 4f, LightEnergy = 1.5f, ShadowEnabled = false });
    }
}
