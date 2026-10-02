using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Items;

/// <summary>
/// Draws dropped items as upright, gently bobbing icon sprites lit by the voxel light at their position; items that
/// glow (Hearts, Stars) hover, pulse and shine, and floating items (souls) bob in mid-air.
/// Visuals are pooled; the simulation lives in the Core <see cref="DroppedItemSystem"/>.
/// </summary>
public sealed partial class DroppedItemRenderer : Node3D
{
    private static readonly Shader SpriteShader = GD.Load<Shader>("res://shaders/sprite_lit.gdshader");
    private readonly Dictionary<long, MeshInstance3D> _visuals = new();
    private readonly Stack<MeshInstance3D> _pool = new();
    private readonly Dictionary<string, ShaderMaterial> _materials = new();
    private readonly QuadMesh _quad = new() { Size = new Vector2(1f, 1f), CenterOffset = new Vector3(0, 0.5f, 0) };
    private GameWorld _game = null!;
    private float _time;

    public void Initialize(GameWorld game)
    {
        _game = game;
        Name = "DroppedItems";
        game.Drops.Removed += item =>
        {
            if (!_visuals.Remove(item.Id, out var mi)) return;
            mi.Visible = false;
            _pool.Push(mi);
        };
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.drops");
        _time += (float)delta;
        var world = _game.Session.World;
        foreach (var item in _game.Drops.Items)
        {
            if (!_visuals.TryGetValue(item.Id, out var mi))
            {
                mi = _pool.Count > 0 ? _pool.Pop() : CreateVisual();
                mi.MaterialOverride = MaterialFor(item.Stack.Item!);
                mi.Visible = true;
                _visuals[item.Id] = mi;
            }
            var p = item.Position;
            var def = item.Stack.Item!;
            float phase = _time * 2.5f + item.Id;
            // Glowing pickups (Hearts, Stars) hover higher and pulse like Terraria's; floating items (souls) bob in the air.
            bool floats = def.Glows || def.NoGravity;
            float bob = item.Grounded || def.NoGravity ? (floats ? 0.18f : 0.05f) + (floats ? 0.08f : 0.04f) * Mathf.Sin(phase) : 0f;
            float pulse = def.Glows ? 1f + 0.12f * Mathf.Sin(_time * 5f + item.Id) : 1f;
            float size = (def.Category == ItemCategory.Block ? 0.3f : 0.38f) * pulse;
            mi.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * size), new Vector3(p.X, p.Y - 0.15f + bob, p.Z));
            mi.SetInstanceShaderParameter("voxel_light", LightProbe.SampleSmooth(world, mi.Position + Vector3.Up * 0.1f));
            mi.SetInstanceShaderParameter("glow", def.Glows ? 0.9f + 0.4f * Mathf.Sin(_time * 5f + item.Id) : def.Rarity > 0 ? 0.08f : 0f);
        }
    }

    private MeshInstance3D CreateVisual()
    {
        var mi = new MeshInstance3D { Mesh = _quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        return mi;
    }

    private ShaderMaterial MaterialFor(ItemDef item)
    {
        if (_materials.TryGetValue(item.Id, out var m)) return m;
        m = new ShaderMaterial { Shader = SpriteShader };
        m.SetShaderParameter("icon", ItemIcons.Get(item));
        _materials[item.Id] = m;
        return m;
    }
}
