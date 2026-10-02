using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;

namespace Strata.Game.Player;

/// <summary>
/// Worn movement gear on the avatar: wings on the back (they flap while flying and spread while gliding), flames
/// under rocket boots and a puff of cloud on every extra jump.
/// </summary>
public sealed partial class PlayerAvatar
{
    private Node3D? _wings;
    private ItemDef? _wingItem;
    private CpuParticles3D? _flames;
    private CpuParticles3D? _puff;
    private float _flap;

    private void InitializeGear()
    {
        _flames = Particles(new Color(1f, 0.85f, 0.35f), 0.2f, 70, 0.3f);
        _flames.Direction = Vector3.Down;
        _flames.Spread = 14f;
        _flames.InitialVelocityMin = 3f;
        _flames.InitialVelocityMax = 5f;
        _flames.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
        _flames.EmissionSphereRadius = 0.09f;
        _flames.ColorRamp = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.15f, 0.9f), new Color(0.5f, 0.25f, 0.2f, 0f) },
        };
        _flames.Emitting = false;
        _puff = Particles(new Color(0.95f, 0.97f, 1f, 0.85f), 0.22f, 24, 0.6f);
        _puff.OneShot = true;
        _puff.Explosiveness = 0.95f;
        _puff.Direction = Vector3.Down;
        _puff.Spread = 80f;
        _puff.InitialVelocityMin = 1f;
        _puff.InitialVelocityMax = 2.5f;
        _puff.Emitting = false;
        _controller.AirJumped += () =>
        {
            _puff.Restart();
            _puff.Emitting = true;
        };
    }

    private CpuParticles3D Particles(Color color, float size, int amount, float lifetime)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoTexture = SoftDot,
        };
        var p = new CpuParticles3D
        {
            Amount = amount, Lifetime = lifetime, Mesh = new QuadMesh { Size = Vector2.One * size, Material = mat },
            Gravity = Vector3.Zero, Color = color, Position = new Vector3(0, 0.05f, 0),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.2f,
        };
        p.ColorRamp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { color, new Color(color, 0f) } };
        AddChild(p);
        return p;
    }

    /// <summary>A round, soft-edged particle sprite.</summary>
    private static readonly GradientTexture2D SoftDot = new()
    {
        Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient { Offsets = new[] { 0f, 0.5f, 1f }, Colors = new[] { Colors.White, new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0) } },
    };

    private void UpdateGear(double delta)
    {
        if (_flames != null) _flames.Emitting = _controller.RocketBurning;
        var wings = WornWings();
        if (wings != _wingItem)
        {
            _wingItem = wings;
            _wings?.QueueFree();
            _wings = null;
            var path = wings == null ? null : AssetPaths.ItemModel(wings, _game.Session.Content);
            if (path != null && _props.Model(path, multimesh: false) is { } mesh)
            {
                // Modelled upright with the wings spread along ±X; worn on the back (+Z faces behind the avatar).
                _wings = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, 0.98f, 0.15f), Scale = Vector3.One * 2.2f };
                AddChild(_wings);
            }
        }
        if (_wings == null) return;
        // Flap while flying, hold the wings wide while gliding, fold them a little on the ground.
        float target = _controller.Flying ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_flap += (float)delta * 16f)) : _controller.Gliding ? 1.1f : _controller.IsOnFloor() ? 0.7f : 0.95f;
        var s = _wings.Scale;
        s.X = Mathf.Lerp(s.X, 2.2f * target, 1f - Mathf.Exp(-(float)delta * 20f));
        _wings.Scale = s;
    }

    private ItemDef? WornWings()
    {
        var inv = _controller.Entity.State.Inventory;
        for (int i = 0; i < inv.AccessorySlotsUnlocked; i++)
        {
            var item = inv.Accessories[i].Item;
            if (item == null) continue;
            foreach (var e in item.EquipEffects)
                if (e.Type == EffectSpecs.Flight && !(e.Raw.TryGetProperty("boots", out var boots) && boots.ValueKind == System.Text.Json.JsonValueKind.True)) return item;
        }
        return null;
    }
}
