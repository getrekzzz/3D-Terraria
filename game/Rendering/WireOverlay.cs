using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// Shows red wire (drawn through blocks) while the local player holds a wiring item, as in the reference game:
/// a small node in each wired cell and a bar to each wired neighbour. Also plays the dart-trap sound on signals.
/// </summary>
public sealed partial class WireOverlay : Node3D
{
    public const int MaxInstances = 6000;
    private const int Radius = 48;

    private static readonly HashSet<string> WiringItems = new() { "wire", "red_wrench", "wire_cutter", "lever", "switch", "pressure_plate", "dart_trap" };

    private GameWorld _game = null!;
    private PlayerEntity _player = null!;
    private MultiMeshInstance3D _nodes = null!;
    private bool _dirty = true;
    private Int3 _center;

    public void Initialize(GameWorld game, PlayerEntity player)
    {
        Name = "WireOverlay";
        _game = game;
        _player = player;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.95f, 0.15f, 0.12f, 0.9f), NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 9,
        };
        _nodes = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = Vector3.One, Material = material }, InstanceCount = MaxInstances, VisibleInstanceCount = 0 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_nodes);
        game.Wires.Changed += () => _dirty = true;
        game.Wires.Signaled += objects =>
        {
            foreach (var o in objects)
                if (o.Def.Trap != null)
                    Audio.Sfx.PlayAt("dart_trap", new Vector3(o.Anchor.X + 0.5f, o.Anchor.Y + 0.5f, o.Anchor.Z + 0.5f) * Units.CellSize);
        };
    }

    public override void _Process(double delta)
    {
        var held = _player.State.Inventory.Held.Item;
        bool show = held != null && WiringItems.Contains(held.Id);
        _nodes.Visible = show;
        if (!show) return;
        var feet = _player.FeetCell;
        if (!_dirty && (feet - _center).ManhattanLength < 8) return;
        _dirty = false;
        _center = feet;
        Rebuild();
    }

    private void Rebuild()
    {
        var mm = _nodes.Multimesh;
        const float s = Units.CellSize;
        int count = 0;
        foreach (var c in _game.Wires.All)
        {
            if (count >= MaxInstances - 3) break;
            if (System.Math.Abs(c.X - _center.X) > Radius || System.Math.Abs(c.Y - _center.Y) > Radius || System.Math.Abs(c.Z - _center.Z) > Radius) continue;
            var mid = (new Vector3(c.X, c.Y, c.Z) + Vector3.One * 0.5f) * s;
            mm.SetInstanceTransform(count++, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.12f), mid));
            // Bars towards the +X, +Y and +Z neighbours (each link drawn once).
            foreach (var (o, axis) in new[] { (new Int3(1, 0, 0), Vector3.Right), (new Int3(0, 1, 0), Vector3.Up), (new Int3(0, 0, 1), Vector3.Back) })
            {
                if (!_game.Wires.Has(c + o)) continue;
                var scale = new Vector3(0.06f, 0.06f, 0.06f) + axis.Abs() * (s - 0.06f);
                mm.SetInstanceTransform(count++, new Transform3D(Basis.Identity.Scaled(scale), mid + axis * s * 0.5f));
            }
        }
        mm.VisibleInstanceCount = count;
    }
}
