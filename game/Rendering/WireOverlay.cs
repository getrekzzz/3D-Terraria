using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Wiring;
using Strata.Core.Session;

namespace Strata.Game.Rendering;

/// <summary>
/// The wire view, shown while the local player holds a wiring tool or device, as in the reference game: each wire
/// colour drawn through blocks in its own colour (a node per wired cell, a bar to each wired neighbour, offset a little
/// per colour so shared cells show every wire), actuators as small brass studs and switched-off blocks as faint ghosts.
/// One pooled MultiMesh per layer, rebuilt only when wiring changes or the player moves 8+ cells. Also plays the
/// dart-trap and teleport sounds.
/// </summary>
public sealed partial class WireOverlay : Node3D
{
    public const int MaxInstancesPerColor = 4000;
    public const int MaxMarkers = 2000;
    private const int Radius = 48;

    private static readonly Color[] Colors =
    {
        new(0.95f, 0.15f, 0.12f, 0.9f), new(0.2f, 0.45f, 1f, 0.9f), new(0.2f, 0.9f, 0.3f, 0.9f), new(1f, 0.85f, 0.15f, 0.9f),
    };

    // Each colour's wire runs slightly off the cell centre so a cell carrying several shows them side by side.
    private static readonly Vector3[] ColorOffsets = { new(-0.06f, 0, -0.06f), new(0.06f, 0, -0.06f), new(-0.06f, 0, 0.06f), new(0.06f, 0, 0.06f) };
    private static readonly Int3[] Links = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
    private static readonly Vector3[] LinkAxes = { Vector3.Right, Vector3.Up, Vector3.Back };

    private readonly HashSet<string> _wiringItems = new();
    private readonly MultiMeshInstance3D[] _layers = new MultiMeshInstance3D[4];
    private readonly int[] _counts = new int[4];
    private GameWorld _game = null!;
    private PlayerEntity _player = null!;
    private MultiMeshInstance3D _actuators = null!, _ghosts = null!;
    private bool _dirty = true;
    private Int3 _center;

    public void Initialize(GameWorld game, PlayerEntity player)
    {
        Name = "WireOverlay";
        _game = game;
        _player = player;
        CollectWiringItems();
        for (int i = 0; i < 4; i++) _layers[i] = AddLayer(Colors[i], MaxInstancesPerColor, 9);
        _actuators = AddLayer(new Color(0.85f, 0.6f, 0.25f, 0.95f), MaxMarkers, 10);
        _ghosts = AddLayer(new Color(0.75f, 0.8f, 0.9f, 0.18f), MaxMarkers, 8);
        game.Wires.Changed += () => _dirty = true;
        game.Wires.Signaled += objects =>
        {
            foreach (var o in objects)
                if (o.Def.Trap != null)
                    Audio.Sfx.PlayAt("dart_trap", new Vector3(o.Anchor.X + 0.5f, o.Anchor.Y + 0.5f, o.Anchor.Z + 0.5f) * Units.CellSize);
        };
        game.Wires.Teleported += (p, feet) => Audio.Sfx.PlayAt("teleport", new Vector3(feet.X, feet.Y, feet.Z));
    }

    /// <summary>Wiring tools and the items that place wiring devices switch the view on.</summary>
    private void CollectWiringItems()
    {
        var content = _game.Session.Content;
        foreach (var item in content.Items.All)
            if (_game.Uses[item].Wiring != null || item.Id == "wire") _wiringItems.Add(item.Id);
        foreach (var o in content.TileObjects.All)
        {
            var d = o;
            bool device = d.Timer > 0 || d.Teleporter || d.Statue != null || d.LogicGate != null || d.LogicLamp || d.Trap != null || d.PressurePlate
                || d.Interaction == Strata.Core.Content.Defs.TileInteraction.Switch;
            if (device && d.PlacingItemId is { } id) _wiringItems.Add(id);
        }
    }

    private MultiMeshInstance3D AddLayer(Color color, int capacity, int priority)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = priority,
        };
        var layer = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = Vector3.One, Material = material }, InstanceCount = capacity, VisibleInstanceCount = 0 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        };
        AddChild(layer);
        return layer;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.wires");
        var held = _player.State.Inventory.Held.Item;
        bool show = held != null && _wiringItems.Contains(held.Id);
        for (int i = 0; i < 4; i++) _layers[i].Visible = show;
        _actuators.Visible = show;
        _ghosts.Visible = show;
        if (!show) return;
        var feet = _player.FeetCell;
        if (!_dirty && (feet - _center).ManhattanLength < 8) return;
        _dirty = false;
        _center = feet;
        Rebuild();
    }

    private bool Near(Int3 c) =>
        System.Math.Abs(c.X - _center.X) <= Radius && System.Math.Abs(c.Y - _center.Y) <= Radius && System.Math.Abs(c.Z - _center.Z) <= Radius;

    private void Rebuild()
    {
        const float s = Units.CellSize;
        var grid = _game.Wires.Grid;
        for (int i = 0; i < 4; i++) _counts[i] = 0;
        int actuators = 0;
        var studs = _actuators.Multimesh;
        foreach (var (c, mask) in grid.Cells)
        {
            if (!Near(c)) continue;
            var mid = (new Vector3(c.X, c.Y, c.Z) + Vector3.One * 0.5f) * s;
            if ((mask & WireGrid.ActuatorBit) != 0 && actuators < MaxMarkers)
                studs.SetInstanceTransform(actuators++, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.16f), mid));
            for (int k = 0; k < 4; k++)
            {
                if ((mask & (1 << k)) == 0 || _counts[k] >= MaxInstancesPerColor - 3) continue;
                var mm = _layers[k].Multimesh;
                var at = mid + ColorOffsets[k];
                mm.SetInstanceTransform(_counts[k]++, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.1f), at));
                // Bars towards the +X, +Y and +Z neighbours of the same colour (each link drawn once).
                for (int l = 0; l < 3; l++)
                {
                    if ((grid.MaskAt(c + Links[l]) & (1 << k)) == 0) continue;
                    var axis = LinkAxes[l];
                    var scale = new Vector3(0.05f, 0.05f, 0.05f) + axis.Abs() * (s - 0.05f);
                    mm.SetInstanceTransform(_counts[k]++, new Transform3D(Basis.Identity.Scaled(scale), at + axis * s * 0.5f));
                }
            }
        }
        for (int k = 0; k < 4; k++) _layers[k].Multimesh.VisibleInstanceCount = _counts[k];
        studs.VisibleInstanceCount = actuators;
        int ghosts = 0;
        var ghostMesh = _ghosts.Multimesh;
        foreach (var c in grid.Inactive.Keys)
        {
            if (ghosts >= MaxMarkers) break;
            if (!Near(c)) continue;
            var mid = (new Vector3(c.X, c.Y, c.Z) + Vector3.One * 0.5f) * s;
            ghostMesh.SetInstanceTransform(ghosts++, new Transform3D(Basis.Identity.Scaled(Vector3.One * (s * 0.96f)), mid));
        }
        ghostMesh.VisibleInstanceCount = ghosts;
    }
}
