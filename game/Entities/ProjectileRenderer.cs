using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Projectiles: arrows use their item model aligned with the flight direction; magic bolts are glowing orbs
/// with a dynamic light (the plan reserves dynamic lights for moving sources like these).
/// </summary>
public sealed partial class ProjectileRenderer : Node3D
{
    private readonly Dictionary<long, Node3D> _views = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;

    public void Initialize(GameWorld game, PropLibrary props)
    {
        _game = game;
        _props = props;
        Name = "Projectiles";
        game.Projectiles.Removed += p =>
        {
            if (_views.Remove(p.Id, out var node)) node.QueueFree();
        };
    }

    public override void _Process(double delta)
    {
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
            node.Position = pos;
            if (vel.LengthSquared() > 0.01f)
            {
                // Models point along +Y: rotate +Y onto the velocity.
                var dir = vel.Normalized();
                var axis = Vector3.Up.Cross(dir);
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(dir), -1f, 1f));
                node.Basis = axis.LengthSquared() > 1e-6f ? new Basis(axis.Normalized(), angle) : (dir.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity);
            }
        }
    }

    private Node3D Create(Projectile p)
    {
        var root = new Node3D();
        var color = Color.FromHtml(p.Def.Color);
        var content = _game.Session.Content;
        Mesh? mesh = null;
        if (p.Def.Model != null)
            mesh = _props.Model(AssetPaths.ProjectileModel(p.Def), multimesh: false);
        else if (p.Def.Visual == "arrow" && content.Items.TryGet(p.Def.Id, out var ammo) && AssetPaths.ItemModel(ammo, content) is { } path)
            mesh = _props.Model(path, multimesh: false); // ammo projectiles reuse their ammo item's model
        if (mesh != null)
        {
            root.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, -0.1f, 0) });
        }
        else
        {
            var orb = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = p.Def.Radius * 0.25f, Height = p.Def.Radius * 0.5f, RadialSegments = 10, Rings = 6 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(orb);
        }
        if (p.Def.Light is { Length: 3 } l)
            root.AddChild(new OmniLight3D { LightColor = new Color(l[0] / 15f, l[1] / 15f, l[2] / 15f), OmniRange = 4f, LightEnergy = 1.5f, ShadowEnabled = false });
        return root;
    }
}
