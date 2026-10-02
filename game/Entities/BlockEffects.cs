using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Mining feedback: a puff of dust in the block's colour on every pickaxe hit and a burst of chips when it breaks, lit
/// by the voxel light where they fly (EffectKit), with a lighter and darker spread of the block's colour.
/// </summary>
public sealed partial class BlockEffects : Node3D
{
    private EffectKit _kit = null!;
    private ulong _lastPlaceFrame = ulong.MaxValue;

    public void Initialize(GameWorld game, EffectKit kit)
    {
        Name = "BlockEffects";
        _kit = kit;
        game.Editor.Struck += OnStruck;
        game.Editor.ObjectHit += OnObjectHit;
        game.Editor.ObjectBroken += obj => ObjectBurst(obj, broke: true);
        game.Editor.BlockPlaced += (cell, def) =>
        {
            // One puff per placement (a grid block reports each of its cells).
            if (Engine.GetProcessFrames() == _lastPlaceFrame) return;
            _lastPlaceFrame = Engine.GetProcessFrames();
            _kit.Burst(EffectRecipe.Dust, CellCenter(cell), Color.FromHtml(def.Color), 6, 1.2f);
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
        bool tink = !broke && outcome.TooHard.Count > 0 && outcome.Damaged.Count == 0;
        if (tink)
        {
            _kit.Burst(EffectRecipe.Sparks, center, new Color(1f, 0.85f, 0.5f), 5, 3f);
            return;
        }
        _kit.Burst(EffectRecipe.Chips, center, color, broke ? 22 : 7, broke ? 3.2f : 1.8f);
        _kit.Burst(EffectRecipe.Dust, center, color.Lightened(0.15f), broke ? 10 : 4, 1.2f);
    }

    private void OnObjectHit(Strata.Core.Voxels.TileObjects.TileObjectInstance obj, ObjectHitResult result)
    {
        if (result == ObjectHitResult.Damaged) ObjectBurst(obj, broke: false);
    }

    /// <summary>Chips from an object struck (5) or broken (20) in its dust colour; trees also shed leaves (8, 30).</summary>
    private void ObjectBurst(Strata.Core.Voxels.TileObjects.TileObjectInstance obj, bool broke)
    {
        var b = obj.Bounds;
        var def = obj.Def;
        float height = def.Topple ? Mathf.Min(b.Max.Y - b.Min.Y, 6) : (b.Max.Y - b.Min.Y) * 0.5f;
        var center = new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Min.Y + height * 0.5f, (b.Min.Z + b.Max.Z) * 0.5f) * Units.CellSize;
        var dust = Color.FromHtml(def.DustColor ?? (def.HitSound == "stone" ? "#7a7a80" : "#8a6040"));
        _kit.Burst(EffectRecipe.Chips, center, dust, broke ? 20 : 5, broke ? 3f : 2f);
        if (def.Leaves == null) return;
        var canopy = new Vector3(center.X, (b.Max.Y - 2) * Units.CellSize, center.Z);
        _kit.Burst(EffectRecipe.Leaves, canopy, Color.FromHtml(def.Leaves), broke ? 30 : 8, 1.5f);
    }

    private static IEnumerable<Int3> DamagedCells(MineOutcome o)
    {
        foreach (var (c, _) in o.Damaged) yield return c;
        foreach (var c in o.TooHard) yield return c;
    }

    private static Vector3 CellCenter(Int3 c) => (new Vector3(c.X, c.Y, c.Z) + Vector3.One * 0.5f) * Units.CellSize;
}
