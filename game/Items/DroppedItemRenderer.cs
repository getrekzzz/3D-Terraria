using System.Collections.Generic;
using Godot;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Items;

/// <summary>
/// Draws dropped items (P3-36) as the real item, slowly spinning and bobbing: its model (PropLibrary, loaded on a loader
/// thread, at most one new model built per frame), a textured cube for blocks (BlockItemMeshes), or its icon as a
/// billboard until the model is ready or when it has none. Rare and glowing items (Hearts, Stars, Fallen Stars) get an
/// additive halo in their rarity or glow colour, and the nearest light-giving ones claim lights from the dynamic pool
/// (<see cref="Lights"/>, at most the preset's <c>dropLights</c>). Everything is lit by the voxel light at the item,
/// refreshed round-robin (a quarter of the drops per frame). Visuals are pooled; the simulation is the Core
/// <see cref="Strata.Core.Gameplay.Items.DroppedItemSystem"/>.
/// </summary>
public sealed partial class DroppedItemRenderer : Node3D
{
    public static DroppedItemRenderer? Instance { get; private set; }

    private readonly Dictionary<long, Visual> _visuals = new();
    private readonly Stack<Visual> _pool = new();
    private GameWorld _game = null!;
    private PropLibrary _props = null!;
    private RenderTables _tables = null!;
    private float _time;
    private int _frame;
    private int _buildsLeft;
    private int _lightBudget;

    public void Initialize(GameWorld game, PropLibrary props, RenderTables tables, GraphicsSettings graphics)
    {
        _game = game;
        _props = props;
        _tables = tables;
        Name = "DroppedItems";
        Instance = this;
        _lightBudget = graphics.Preset.DropLights;
        graphics.Changed += p => _lightBudget = p.DropLights;
        game.Drops.Removed += item =>
        {
            if (!_visuals.Remove(item.Id, out var v)) return;
            v.Hide();
            _pool.Push(v);
        };
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.drops");
        _time += (float)delta;
        _frame++;
        _buildsLeft = 1;
        var world = _game.Session.World;
        var items = _game.Drops.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            bool fresh = false;
            if (!_visuals.TryGetValue(item.Id, out var v))
            {
                v = _pool.Count > 0 ? _pool.Pop() : CreateVisual();
                _visuals[item.Id] = v;
                Setup(v, item.Stack.Item!);
                fresh = true;
            }
            else if (v.Awaiting && TryUpgrade(v)) fresh = true;
            Place(v, item);
            if (fresh || (i & 3) == (_frame & 3)) v.SetLight(LightProbe.SampleSmooth(world, v.Center + Vector3.Up * 0.1f));
        }
    }

    /// <summary>Probe counts: drops drawn as models, block cubes, billboards; halos shown; drops that may claim a light.</summary>
    public (int Models, int Cubes, int Billboards, int Halos, int Lit) Counts()
    {
        int models = 0, cubes = 0, cards = 0, halos = 0, lit = 0;
        foreach (var v in _visuals.Values)
        {
            switch (v.Kind)
            {
                case VisualKind.Model: models++; break;
                case VisualKind.Cube: cubes++; break;
                default: cards++; break;
            }
            if (v.HasHalo) halos++;
            if (v.LightPeak > 0f) lit++;
        }
        return (models, cubes, cards, halos, System.Math.Min(lit, _lightBudget));
    }
}
