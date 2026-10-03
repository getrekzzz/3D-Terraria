using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.Entities;

/// <summary>
/// Mining feedback: a puff of dust in the block's colour on every pickaxe hit and a burst of chips when it breaks; wood
/// chips fly from the foot of a tree's trunk on every axe chop and in a shower when it falls. Emitters are pooled one-shot
/// CPU particles.
/// </summary>
public sealed partial class BlockEffects : Node3D
{
    private readonly Queue<CpuParticles3D> _pool = new();
    private StandardMaterial3D _material = null!;
    private BoxMesh _chip = null!;
    private ulong _lastPlaceFrame = ulong.MaxValue;

    public void Initialize(GameWorld game)
    {
        Name = "BlockEffects";
        _material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };
        _chip = new BoxMesh { Size = Vector3.One * 0.07f, Material = _material };
        game.Editor.Struck += OnStruck;
        game.Editor.ObjectDamaged += obj =>
        {
            if (obj.Def.Collision == TileCollision.Trunk) Burst(ChopPoint(obj), WoodColor(obj.Def), 9, 2.2f, 0.55);
        };
        game.Editor.ObjectBroken += obj =>
        {
            if (obj.Def.Collision == TileCollision.Trunk) Burst(ChopPoint(obj), WoodColor(obj.Def), 26, 3.4f, 0.9);
        };
        game.Editor.BlockPlaced += (cell, def) =>
        {
            // One puff per placement (a grid block reports each of its cells).
            if (Engine.GetProcessFrames() == _lastPlaceFrame) return;
            _lastPlaceFrame = Engine.GetProcessFrames();
            Burst(CellCenter(cell), Color.FromHtml(def.Color), 6, 1.2f, 0.4);
        };
    }

    private void OnStruck(MineOutcome outcome)
    {
        var cells = outcome.Broken.Count > 0 ? outcome.Broken : null;
        var center = Vector3.Zero;
        int n = 0;
        foreach (var c in cells ?? (IEnumerable<Int3>)DamagedCells(outcome))
        {
            center += CellCenter(c);
            n++;
        }
        if (n == 0) return;
        center /= n;
        var color = Color.FromHtml(outcome.Material!.Color);
        bool broke = outcome.Broken.Count > 0;
        Burst(center, color, broke ? 22 : outcome.TooHard.Count > 0 && outcome.Damaged.Count == 0 ? 3 : 7, broke ? 3.2f : 1.8f, broke ? 0.9 : 0.5);
    }

    private static IEnumerable<Int3> DamagedCells(MineOutcome o)
    {
        foreach (var (c, _) in o.Damaged) yield return c;
        foreach (var c in o.TooHard) yield return c;
    }

    private void Burst(Vector3 at, Color color, int amount, float speed, double life)
    {
        var p = _pool.Count > 0 ? _pool.Dequeue() : Create();
        p.GlobalPosition = at;
        p.Amount = amount;
        p.Lifetime = life;
        p.InitialVelocityMin = speed * 0.4f;
        p.InitialVelocityMax = speed;
        p.Color = color.Darkened(0.1f);
        p.Restart();
        p.Emitting = true;
    }

    private CpuParticles3D Create()
    {
        var p = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Explosiveness = 0.9f,
            Direction = Vector3.Up,
            Spread = 75f,
            Gravity = new Vector3(0, -12f, 0),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.3f,
            AngularVelocityMin = -360f,
            AngularVelocityMax = 360f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = Vector3.One * 0.25f,
            Mesh = _chip,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.Finished += () => _pool.Enqueue(p);
        return p;
    }

    /// <summary>The middle of the chopped part of the trunk (about waist height).</summary>
    private static Vector3 ChopPoint(TileObjectInstance obj)
    {
        var (min, max) = World.ObjectColliders.HitBox(obj);
        return (min + max) * 0.5f * Units.CellSize;
    }

    /// <summary>Bark and heartwood tints by tree kind (objects carry no colour of their own).</summary>
    private static Color WoodColor(TileObjectDef def) => def.Id switch
    {
        "cactus" => new Color(0.36f, 0.55f, 0.24f),
        "giant_glowing_mushroom" => new Color(0.5f, 0.62f, 0.8f),
        "ebonwood_tree" => new Color(0.38f, 0.3f, 0.44f),
        "mahogany_tree" => new Color(0.5f, 0.24f, 0.18f),
        "palm_tree" => new Color(0.62f, 0.49f, 0.33f),
        "pine_tree" => new Color(0.42f, 0.31f, 0.22f),
        _ => new Color(0.5f, 0.35f, 0.21f),
    };

    private static Vector3 CellCenter(Int3 c) => (new Vector3(c.X, c.Y, c.Z) + Vector3.One * 0.5f) * Units.CellSize;
}
