using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;
using Strata.Game.Rendering;

namespace Strata.Game.World;

/// <summary>
/// Trees answer the axe: each chop makes the tree shiver (its instance is hidden and a copy wobbles ±3° about the base,
/// damped, for 0.18 s), and a felled tree topples away from the player over 0.9 s, crashes with a thud, a burst of
/// leaves and wood chips along the trunk and a small camera kick, then sinks away. Up to three of each at once, pooled;
/// the tree models are loaded at world start so the first chop does not hitch.
/// </summary>
public sealed partial class TreeEffects : Node3D
{
    private const int Slots = 3;
    private const float WobbleTime = 0.18f, FallTime = 0.9f, SinkTime = 0.3f;
    private static readonly StringName LightParam = "voxel_light";

    private sealed class Copy
    {
        public MeshInstance3D Node = null!;
        public TileObjectInstance? Obj;
        public Transform3D Base;
        public Vector3 Axis, Pivot;
        public float Age = -1f, Height;
        public Color Leaves, Chips;
        public bool Landed;
    }

    private GameWorld _game = null!;
    private TileObjectRenderer _objects = null!;
    private readonly Copy[] _wobbles = new Copy[Slots], _falls = new Copy[Slots];

    public void Initialize(GameWorld game, TileObjectRenderer objects)
    {
        Name = "TreeEffects";
        _game = game;
        _objects = objects;
        for (int i = 0; i < Slots; i++)
        {
            _wobbles[i] = NewCopy();
            _falls[i] = NewCopy();
        }
        foreach (var def in game.Session.Content.TileObjects.All)
            if (def.Topple)
                for (int v = 0; v < def.Variants; v++) objects.Props.Model(Strata.Core.Content.AssetPaths.ObjectModel(def, v), multimesh: false);
        game.Editor.ObjectHit += OnHit;
    }

    private Copy NewCopy()
    {
        var node = new MeshInstance3D { Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(node);
        return new Copy { Node = node };
    }

    private void OnHit(TileObjectInstance obj, ObjectHitResult result)
    {
        if (obj.Def.SizeY < 4) return;
        if (result == ObjectHitResult.Damaged) Wobble(obj);
        else if (result == ObjectHitResult.Broken && obj.Def.Topple) Fall(obj);
    }

    private void Wobble(TileObjectInstance obj)
    {
        foreach (var w in _wobbles)
            if (w.Obj == obj)
            {
                w.Age = 0f; // a new chop restarts its shiver
                return;
            }
        var slot = Free(_wobbles);
        if (slot.Obj != null) _objects.ShowInstance(slot.Obj);
        if (!Setup(slot, obj) || !_objects.HideInstance(obj))
        {
            slot.Node.Visible = false;
            slot.Obj = null;
            return;
        }
        var away = AwayFromPlayer(slot.Pivot);
        slot.Axis = new Vector3(away.Z, 0, -away.X);
    }

    private void Fall(TileObjectInstance obj)
    {
        foreach (var w in _wobbles)
            if (w.Obj == obj)
            {
                w.Obj = null;
                w.Age = -1f;
                w.Node.Visible = false;
            }
        var slot = Free(_falls);
        if (!Setup(slot, obj)) return;
        var away = AwayFromPlayer(slot.Pivot);
        slot.Axis = new Vector3(away.Z, 0, -away.X);
        slot.Landed = false;
    }

    private bool Setup(Copy c, TileObjectInstance obj)
    {
        var mesh = _objects.Props.Model(Strata.Core.Content.AssetPaths.ObjectModel(obj.Def, obj.Variant), multimesh: false);
        if (mesh == null) return false;
        c.Obj = obj;
        c.Age = 0f;
        c.Base = _objects.Placement.TransformOf(obj);
        var fp = obj.Footprint;
        c.Pivot = new Vector3(obj.Anchor.X + fp.X * 0.5f, obj.Anchor.Y, obj.Anchor.Z + fp.Z * 0.5f) * Units.CellSize;
        c.Height = fp.Y * Units.CellSize;
        c.Leaves = obj.Def.Leaves != null ? Color.FromHtml(obj.Def.Leaves) : new Color(0.3f, 0.55f, 0.2f);
        c.Chips = obj.Def.DustColor != null ? Color.FromHtml(obj.Def.DustColor) : new Color(0.54f, 0.38f, 0.25f);
        c.Node.Mesh = mesh;
        c.Node.GlobalTransform = c.Base;
        c.Node.SetInstanceShaderParameter(LightParam, LightProbe.Sample(_game.Session.World, c.Pivot + Vector3.Up));
        c.Node.Visible = true;
        return true;
    }

    private Vector3 AwayFromPlayer(Vector3 pivot)
    {
        if (_game.LocalPlayer is not { } p) return Vector3.Right;
        var d = pivot - new Vector3(p.Position.X, pivot.Y, p.Position.Z);
        return d.LengthSquared() > 1e-4f ? d.Normalized() : Vector3.Right;
    }

    private static Copy Free(Copy[] slots)
    {
        Copy best = slots[0];
        foreach (var s in slots)
        {
            if (s.Age < 0f) return s;
            if (s.Age > best.Age) best = s;
        }
        return best;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var w in _wobbles)
        {
            if (w.Age < 0f || w.Obj == null) continue;
            w.Age += dt;
            if (w.Age >= WobbleTime)
            {
                _objects.ShowInstance(w.Obj);
                w.Node.Visible = false;
                w.Obj = null;
                w.Age = -1f;
                continue;
            }
            float k = w.Age / WobbleTime;
            float angle = Mathf.DegToRad(3f) * Mathf.Sin(k * Mathf.Pi * 3f) * (1f - k);
            w.Node.GlobalTransform = Rotated(w.Base, w.Pivot, w.Axis, angle);
        }
        foreach (var f in _falls)
        {
            if (f.Age < 0f) continue;
            f.Age += dt;
            if (f.Age < FallTime)
            {
                float k = f.Age / FallTime;
                f.Node.GlobalTransform = Rotated(f.Base, f.Pivot, f.Axis, Mathf.Pi * 0.5f * k * k); // ease in: it gathers speed
                continue;
            }
            var down = Rotated(f.Base, f.Pivot, f.Axis, Mathf.Pi * 0.5f);
            if (!f.Landed)
            {
                f.Landed = true;
                Land(f);
            }
            float s = 1f - (f.Age - FallTime) / SinkTime;
            if (s <= 0f)
            {
                f.Node.Visible = false;
                f.Age = -1f;
                f.Obj = null;
                continue;
            }
            f.Node.GlobalTransform = new Transform3D(down.Basis, down.Origin + Vector3.Down * (1f - s) * 0.6f);
            f.Node.Transparency = 1f - s;
        }
    }

    /// <summary>The crash: thud, leaves and chips along the fallen trunk, and a kick if close.</summary>
    private void Land(Copy f)
    {
        var along = f.Axis.Cross(Vector3.Up).Normalized() * -1f;
        var mid = f.Pivot + along * f.Height * 0.5f + Vector3.Up * 0.4f;
        Audio.Sfx.PlayAt("tree_fall", mid);
        var kit = EffectKit.Instance;
        kit?.Burst(EffectRecipe.Leaves, f.Pivot + along * f.Height * 0.8f + Vector3.Up * 0.8f, f.Leaves, 30, 2.5f);
        kit?.Burst(EffectRecipe.Chips, mid, f.Chips, 20, 2.5f);
        if (_game.LocalPlayer is { } p && new Vector3(p.Position.X, p.Position.Y, p.Position.Z).DistanceTo(mid) < 12f) Player.CameraShake.Kick(0.1f);
        f.Node.Transparency = 0f;
    }

    private static Transform3D Rotated(Transform3D xf, Vector3 pivot, Vector3 axis, float angle)
    {
        var rot = new Basis(axis.Normalized(), angle);
        return new Transform3D(rot * xf.Basis, pivot + rot * (xf.Origin - pivot));
    }
}
