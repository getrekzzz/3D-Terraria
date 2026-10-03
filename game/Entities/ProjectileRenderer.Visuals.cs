using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// How a projectile looks (P3-11): its model (creature, item, projectile model or ammo item), a bullet streak, or a
/// glowing bolt (a core of at least 6 cm and an additive halo four times its size); its spin from data
/// (<c>spin</c> rad/s about <c>spinAxis</c>: tumble end over end, flat like a disc, roll about the flight; boomerangs
/// and fused throws spin by default). Materials and meshes are shared (ProjectileRenderer.Pool).
/// </summary>
public sealed partial class ProjectileRenderer
{
    private static QuadMesh? _haloQuad;
    private static readonly Dictionary<Color, StandardMaterial3D> Halos = new();

    /// <summary>The flight-aligned basis turned by the projectile's spin.</summary>
    private Basis Spun(Projectile p, Basis along)
    {
        float spin = p.Def.Spin != 0 ? p.Def.Spin : p.Def.Returns > 0 ? 22f : p.Def.Fuse ? 6f : 0f;
        if (spin == 0f) return along;
        string axis = p.Def.Spin != 0 ? p.Def.SpinAxis : p.Def.Returns > 0 ? "flat" : "tumble";
        float turn = _clock * spin + (p.Id % 16) * 0.4f; // each throw starts at its own angle
        return axis switch
        {
            "flat" => new Basis(Vector3.Up, turn) * new Basis(Vector3.Right, Mathf.Pi / 2f), // laid flat, whirling about the vertical
            "roll" => along * new Basis(Vector3.Up, turn), // about the flight (models point +Y along it)
            _ => along * new Basis(Vector3.Right, turn), // end over end
        };
    }

    /// <summary>An additive glow billboard around a bolt (shared quad, one material per colour).</summary>
    private static MeshInstance3D Halo(Color color, float radius)
    {
        _haloQuad ??= new QuadMesh { Size = Vector2.One };
        if (!Halos.TryGetValue(color, out var material))
        {
            Halos[color] = material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoTexture = HaloTexture(), AlbedoColor = new Color(color, 0.7f),
            };
        }
        return new MeshInstance3D { Mesh = _haloQuad, MaterialOverride = material, Scale = Vector3.One * radius * 2f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    private static ImageTexture? _haloTexture;

    /// <summary>A soft round falloff, bright in the middle.</summary>
    private static ImageTexture HaloTexture()
    {
        if (_haloTexture != null) return _haloTexture;
        const int n = 32;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp(1f - Mathf.Sqrt(u * u + v * v), 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        return _haloTexture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Builds a projectile's view from scratch (probe <c>projectiles.nodes_created</c> counts these).</summary>
    private Node3D Build(Projectile p)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.projectiles.create");
        NodesCreated++;
        var root = new Node3D();
        var color = Color.FromHtml(p.Def.Color);
        var content = _game.Session.Content;
        if (p.Def.Visual == "creature" && CreatureVisual.Create($"assets/models/creatures/{p.Def.Model ?? p.Def.Id}.glb", _props) is { } creature)
        {
            creature.Scale = Vector3.One * p.Def.Scale;
            root.AddChild(creature);
            creature.SetTint(color);
            creature.Play("idle");
            return root;
        }
        // Models still loading on a loader thread leave this projectile with the glow fallback (later ones get the model).
        Mesh? mesh = null;
        string? modelPath = null;
        bool creatureLoading = p.Def.Visual == "creature"; // its model is pending: glow until it is ready
        if (creatureLoading)
        {
            modelPath = null;
            _awaiting.Add(p.Id);
        }
        else if (p.Def.Visual == "item" && content.Items.TryGet(p.Def.Model ?? p.Def.Id, out var thrown) && AssetPaths.ItemModel(thrown, content) is { } itemPath)
            modelPath = itemPath;
        else if (p.Def.Model != null)
            modelPath = AssetPaths.ProjectileModel(p.Def);
        else if (p.Def.Visual == "arrow" && content.Items.TryGet(p.Def.Id, out var ammo) && AssetPaths.ItemModel(ammo, content) is { } path)
            modelPath = path; // ammo projectiles reuse their ammo item's model
        if (modelPath != null && _props.TryModel(modelPath, multimesh: false, out mesh) == AssetState.Pending) _awaiting.Add(p.Id);
        if (mesh != null)
        {
            root.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, -0.1f, 0), Scale = Vector3.One * p.Def.Scale });
        }
        else if (p.Def.Visual == "bullet")
        {
            // A short bright streak along the flight direction.
            root.AddChild(new MeshInstance3D
            {
                Mesh = BulletMesh, MaterialOverride = SharedGlow(color, 4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        else
        {
            float r = p.Def.Bobber ? 0.07f : Mathf.Max(0.06f, p.Def.Radius * 0.25f);
            root.AddChild(new MeshInstance3D
            {
                Mesh = Sphere(r),
                MaterialOverride = p.Def.Bobber ? new StandardMaterial3D { AlbedoColor = color, Roughness = 0.4f } : SharedGlow(color, 3f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            if (!p.Def.Bobber) root.AddChild(Halo(color, r * 4f)); // a bolt glows beyond its core
        }
        return root;
    }

    internal static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = energy, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
