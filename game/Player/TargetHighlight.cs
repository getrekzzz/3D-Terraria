using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.Player;

/// <summary>
/// The block cursor: shows exactly what the held item will affect. Mining tools outline the grid block (or single
/// cell in precision mode) they will dig, with cracks spreading as it weakens; blocks show a ghost of what will be
/// placed; objects show their footprint. Outlines stay visible through the terrain so the target always reads.
/// </summary>
public sealed partial class TargetHighlight : Node3D
{
    private static Color Neutral => Color.FromHtml(Settings.UserSettings.Current.CursorColor) with { A = 0.95f };
    private static readonly Color Working = new(1f, 0.78f, 0.25f, 1f);
    private static readonly Color Invalid = new(1f, 0.3f, 0.25f, 0.95f);
    private static readonly Color Place = new(0.45f, 0.9f, 1f, 0.95f);
    private static readonly Color GhostColor = new(0.55f, 0.88f, 1f, 0.3f);

    private GameWorld _game = null!;
    private InteractionController _interaction = null!;
    private PlayerEntity _entity = null!;
    private SelectionMesh _mesh = null!;

    public void Initialize(GameWorld game, InteractionController interaction, PlayerEntity entity)
    {
        _game = game;
        _interaction = interaction;
        _entity = entity;
        Name = "TargetHighlight";
        TopLevel = true;
        _mesh = new SelectionMesh { Name = "Selection" };
        AddChild(_mesh);
        _mesh.Initialize();
    }

    public override void _Process(double delta)
    {
        _mesh.Clear();
        var target = _interaction.Target;
        var held = _entity.State.Inventory.Held;
        if (target == null || !_interaction.InReach) return;
        _mesh.Eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        if (held.IsEmpty)
        {
            _mesh.Commit();
            return;
        }
        var info = _game.Uses[held.Item!];
        if (info.Block is { } block)
            DrawBlockPlacement(target, block, held.Count, info.Item.Consumable);
        else if (info.Object is { } obj)
            DrawObjectGhost(target, obj);
        else if (target.Object is { } o && info.IsTool)
            _mesh.Box(Min(o.Bounds.Min), Min(o.Bounds.Max), Neutral);
        else if (info.Has(ItemAction.Mine))
            DrawMining(target, held.Item!);
        else if (info.Has(ItemAction.Hammer))
            DrawHammer(target);
        _mesh.Commit();
    }

    private IEnumerable<Int3> CellsAt(Int3 cell) => _entity.Precision ? new[] { cell } : BlockGrid.Cells(cell);

    private void DrawMining(Target target, ItemDef tool)
    {
        var world = _game.Session.World;
        var content = _game.Session.Content;
        long tick = _game.Session.Clock.Tick;
        Int3 min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        float progress = 0;
        bool tooHard = false, any = false;
        foreach (var c in CellsAt(target.Cell))
        {
            ushort b = world.GetBlock(c);
            if (b == 0 || b == VoxelWorld.Unknown || content.Blocks[b].Render == BlockRender.None) continue;
            any = true;
            min = Int3.Min(min, c);
            max = Int3.Max(max, c + Int3.One);
            var def = content.Blocks[b];
            if (!MiningRules.CanMine(def, tool.PickPower) || _game.Editor.IsLocked(c)) tooHard = true;
            progress = Mathf.Max(progress, _game.Editor.Damage.DamageAt(c, tick) / MiningRules.Health(def));
        }
        if (!any) return;
        progress = Mathf.Clamp(progress, 0, 1);
        var color = tooHard ? Invalid : progress > 0 ? Neutral.Lerp(Working, 0.4f + 0.6f * progress) : Neutral;
        _mesh.Faces(Min(min), Min(max), new Color(color.R, color.G, color.B, tooHard ? 0.18f : 0.11f), progress);
        _mesh.Box(Min(min), Min(max), color);
    }

    private void DrawHammer(Target target)
    {
        byte shape = ShapeCode.Pack(_entity.BuildShape, _entity.FacingQuarter + _entity.BuildRotation);
        if (_entity.Precision)
        {
            _mesh.Ghost(Min(target.Cell), shape, Units.CellSize, GhostColor);
            _mesh.Box(Min(target.Cell), Min(target.Cell + Int3.One), Neutral);
            return;
        }
        foreach (var (cell, code) in BlockGrid.Pattern(target.Cell, _entity.BuildShape, _entity.FacingQuarter + _entity.BuildRotation))
            _mesh.Ghost(Min(cell), code, Units.CellSize, GhostColor);
        var origin = BlockGrid.MinOf(target.Cell);
        _mesh.Box(Min(origin), Min(origin + Int3.One * BlockGrid.Size), Neutral);
    }

    private void DrawBlockPlacement(Target target, BlockDef block, int count, bool consumable)
    {
        var cell = ItemUseSystem.PlacementCell(target);
        int rotation = _entity.FacingQuarter + _entity.BuildRotation;
        var shape = block.Render == BlockRender.Crisp ? _entity.BuildShape : CellShape.Full;
        List<(Int3 Cell, byte Shape)> cells = _entity.Precision
            ? new() { (cell, block.Render == BlockRender.Crisp ? ShapeCode.Pack(shape, rotation) : (byte)0) }
            : BlockGrid.Pattern(cell, shape, rotation);
        var blockers = _game.Players.Where(p => p.Alive).Select(p => p.CellBox);
        var (result, free) = PlacementRules.CanPlaceBlocks(_game.Session.World, cells, block, consumable ? count : int.MaxValue, blockers);
        bool ok = result == PlacementResult.Ok;
        foreach (var (c, code) in ok ? free : cells)
            _mesh.Ghost(Min(c), block.Render == BlockRender.Crisp ? code : (byte)0, Units.CellSize, ok ? GhostColor : new Color(Invalid, 0.22f));
        var origin = _entity.Precision ? cell : BlockGrid.MinOf(cell);
        int size = _entity.Precision ? 1 : BlockGrid.Size;
        _mesh.Box(Min(origin), Min(origin + Int3.One * size), ok ? Place : Invalid);
    }

    private void DrawObjectGhost(Target target, TileObjectDef def)
    {
        int rotation = (_entity.FacingQuarter + _entity.BuildRotation) & 3;
        var anchor = ItemUseSystem.ObjectAnchor(def, target, rotation);
        var fp = TileObjectInstance.FootprintOf(def, rotation);
        var ok = PlacementRules.CanPlaceObject(_game.Session.World, def, anchor, rotation) == PlacementResult.Ok;
        _mesh.Faces(Min(anchor), Min(anchor + fp), ok ? new Color(Place, 0.12f) : new Color(Invalid, 0.16f), 0f);
        _mesh.Box(Min(anchor), Min(anchor + fp), ok ? Place : Invalid);
    }

    private static Vector3 Min(Int3 c) => new Vector3(c.X, c.Y, c.Z) * Units.CellSize;
}
