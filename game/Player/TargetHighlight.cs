using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mining;
using Strata.Core.Meshing;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.Player;

/// <summary>
/// Shows what the held item will affect: a wire box around the mining brush (tinted by mining progress),
/// the target object's bounds, or a translucent ghost of the block shape or object footprint to be placed.
/// </summary>
public sealed partial class TargetHighlight : Node3D
{
    private static readonly Color Neutral = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Invalid = new(1f, 0.3f, 0.25f, 0.9f);
    private static readonly Color GhostColor = new(0.55f, 0.85f, 1f, 0.28f);

    private GameWorld _game = null!;
    private InteractionController _interaction = null!;
    private PlayerEntity _entity = null!;
    private ImmediateMesh _lines = null!;
    private ImmediateMesh _ghost = null!;

    public void Initialize(GameWorld game, InteractionController interaction, PlayerEntity entity)
    {
        _game = game;
        _interaction = interaction;
        _entity = entity;
        Name = "TargetHighlight";
        TopLevel = true;
        _lines = new ImmediateMesh();
        AddChild(new MeshInstance3D
        {
            Mesh = _lines,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = false,
            },
        });
        _ghost = new ImmediateMesh();
        AddChild(new MeshInstance3D
        {
            Mesh = _ghost,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
    }

    public override void _Process(double delta)
    {
        _lines.ClearSurfaces();
        _ghost.ClearSurfaces();
        var target = _interaction.Target;
        var held = _entity.State.Inventory.Held;
        if (target == null || !_interaction.InReach || held.IsEmpty) return;
        var info = _game.Uses[held.Item!];

        if (info.Block is { } block)
            DrawBlockGhost(target, block);
        else if (info.Object is { } obj)
            DrawObjectGhost(target, obj);
        else if (target.Object is { } o && info.IsTool)
            DrawBox(Min(o.Bounds.Min), Min(o.Bounds.Max), Neutral);
        else if (info.Has(ItemAction.Mine) || info.Has(ItemAction.Hammer))
            DrawBrush(target, held.Item!, info);
    }

    private void DrawBrush(Target target, ItemDef tool, ItemUseInfo info)
    {
        var normal = target.Normal == Int3.Zero ? Int3.Up : target.Normal;
        int size = _entity.Precision || !info.Has(ItemAction.Mine) ? 1 : System.Math.Max(1, tool.MineBrush);
        Int3 min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        float progress = 0;
        bool tooHard = false;
        long tick = _game.Session.Clock.Tick;
        foreach (var c in MiningRules.Brush(target.Cell, normal, target.Fraction, size))
        {
            min = Int3.Min(min, c);
            max = Int3.Max(max, c + Int3.One);
            ushort b = _game.Session.World.GetBlock(c);
            if (b == 0 || b == VoxelWorld.Unknown) continue;
            var def = _game.Session.Content.Blocks[b];
            if (!MiningRules.CanMine(def, tool.PickPower)) tooHard = true;
            progress = Mathf.Max(progress, _game.Editor.Damage.DamageAt(c, tick) / MiningRules.Health(def));
        }
        var color = tooHard ? Invalid : Neutral.Lerp(new Color(1f, 0.75f, 0.2f, 1f), Mathf.Clamp(progress, 0, 1));
        float shrink = progress * 0.08f;
        DrawBox(Min(min) + Vector3.One * shrink, Min(max) - Vector3.One * shrink, color);
    }

    private void DrawBlockGhost(Target target, BlockDef block)
    {
        var cell = target.Normal == Int3.Zero ? target.Cell : target.Adjacent;
        byte code = block.Render == BlockRender.Crisp ? ShapeCode.Pack(_entity.BuildShape, _entity.FacingQuarter + _entity.BuildRotation) : (byte)0;
        var origin = Min(cell);
        _ghost.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (var poly in ShapeGeometry.Get(code))
        {
            for (int i = 1; i + 1 < poly.Points.Length; i++)
            {
                foreach (var p in new[] { poly.Points[0], poly.Points[i], poly.Points[i + 1] })
                {
                    _ghost.SurfaceSetColor(GhostColor);
                    _ghost.SurfaceAddVertex(origin + new Vector3(p.X, p.Y, p.Z) * Units.CellSize);
                }
            }
        }
        _ghost.SurfaceEnd();
        DrawBox(origin, origin + Vector3.One * Units.CellSize, Neutral);
    }

    private void DrawObjectGhost(Target target, TileObjectDef def)
    {
        int rotation = (_entity.FacingQuarter + _entity.BuildRotation) & 3;
        var anchor = ItemUseSystem.ObjectAnchor(def, target, rotation);
        var fp = TileObjectInstance.FootprintOf(def, rotation);
        var ok = Strata.Core.Gameplay.Building.PlacementRules.CanPlaceObject(_game.Session.World, def, anchor, rotation);
        DrawBox(Min(anchor), Min(anchor + fp), ok == Strata.Core.Gameplay.Building.PlacementResult.Ok ? Neutral : Invalid);
    }

    private void DrawBox(Vector3 a, Vector3 b, Color color)
    {
        const float e = 0.004f;
        a -= Vector3.One * e;
        b += Vector3.One * e;
        var c = new[]
        {
            new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, a.Y, a.Z), new Vector3(b.X, a.Y, b.Z), new Vector3(a.X, a.Y, b.Z),
            new Vector3(a.X, b.Y, a.Z), new Vector3(b.X, b.Y, a.Z), new Vector3(b.X, b.Y, b.Z), new Vector3(a.X, b.Y, b.Z),
        };
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        _lines.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (int i in edges)
        {
            _lines.SurfaceSetColor(color);
            _lines.SurfaceAddVertex(c[i]);
        }
        _lines.SurfaceEnd();
    }

    private static Vector3 Min(Int3 c) => new Vector3(c.X, c.Y, c.Z) * Units.CellSize;
}
