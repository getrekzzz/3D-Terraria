using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Items;

/// <summary>The drop visuals: model, block cube or billboard bodies, halos and the light candidates.</summary>
public sealed partial class DroppedItemRenderer
{
    private static readonly Shader SpriteShader = GD.Load<Shader>("res://shaders/sprite_lit.gdshader");
    private static readonly StringName VoxelLightParam = "voxel_light", GlowParam = "glow", HaloParam = "halo_color";

    /// <summary>Spin about Y (rad/s) and the size of a model's largest extent on the ground (m): gear, then stackables.</summary>
    private const float Spin = 1.5f, GearSize = 0.45f, StackSize = 0.32f, CubeScale = 1.4f;

    private readonly Dictionary<string, ShaderMaterial> _materials = new();
    private readonly Dictionary<ItemDef, Look?> _looks = new();
    private readonly QuadMesh _quad = new() { Size = new Vector2(1f, 1f), CenterOffset = new Vector3(0, 0.5f, 0) };
    private readonly QuadMesh _haloQuad = new() { Size = new Vector2(1f, 1f) };
    private readonly ShaderMaterial _haloMaterial = new() { Shader = GD.Load<Shader>("res://shaders/drop_halo.gdshader") };
    private readonly List<Visual> _lit = new();
    private Vector3 _eye;
    private Comparison<Visual>? _byDistance;

    private enum VisualKind { Billboard, Model, Cube }

    /// <summary>How an item's mesh sits on the ground: scale, the offset that centres it, and its half height (m).</summary>
    private sealed record Look(Mesh Mesh, VisualKind Kind, float Scale, Vector3 Offset, float HalfHeight);

    private sealed class Visual
    {
        public MeshInstance3D Body = null!, Card = null!, Halo = null!;
        public ItemDef Item = null!;
        public VisualKind Kind;
        public bool Awaiting;
        public string? ModelPath;
        public Look? Look;
        public bool HasHalo, AnimatedHalo;
        public Color HaloColor;
        public float HaloStrength;
        public Color LightColor;
        public float LightPeak;
        public bool Fire;
        public Vector3 Center;
        public readonly Func<Vector3> Track;

        public Visual() => Track = () => Center;

        public void Hide()
        {
            Body.Visible = Card.Visible = Halo.Visible = false;
            HasHalo = false;
            LightPeak = 0f;
        }

        public void SetLight(Color light) => (Kind == VisualKind.Billboard ? Card : Body).SetInstanceShaderParameter(VoxelLightParam, light);
    }

    private Visual CreateVisual()
    {
        var off = GeometryInstance3D.ShadowCastingSetting.Off;
        var v = new Visual
        {
            Body = new MeshInstance3D { CastShadow = off, Visible = false },
            Card = new MeshInstance3D { Mesh = _quad, CastShadow = off, Visible = false },
            Halo = new MeshInstance3D { Mesh = _haloQuad, MaterialOverride = _haloMaterial, CastShadow = off, Visible = false },
        };
        AddChild(v.Body);
        AddChild(v.Card);
        AddChild(v.Halo);
        return v;
    }

    private void Setup(Visual v, ItemDef item)
    {
        v.Item = item;
        v.Awaiting = false;
        v.ModelPath = null;
        SetupGlow(v, item);
        if (_looks.TryGetValue(item, out var known))
        {
            if (known != null) Apply(v, known);
            else UseCard(v);
            return;
        }
        var content = _game.Session.Content;
        if (BlockItemMeshes.ForItem(item, content, _tables) is { } cube)
        {
            var look = new Look(cube, VisualKind.Cube, CubeScale, Vector3.Zero, BlockItemMeshes.Size * CubeScale * 0.5f);
            _looks[item] = look;
            Apply(v, look);
            return;
        }
        var path = AssetPaths.ItemModel(item, content);
        UseCard(v);
        if (path == null || !ResourceLoader.Exists("res://" + path))
        {
            _looks[item] = null; // no model: the icon billboard
            return;
        }
        v.ModelPath = path;
        v.Awaiting = true;
        TryUpgrade(v);
    }

    /// <summary>Swaps a billboard for the item's model once it has loaded (at most one new model built per frame).</summary>
    private bool TryUpgrade(Visual v)
    {
        if (_looks.TryGetValue(v.Item, out var look))
        {
            v.Awaiting = false;
            if (look == null) return false;
            Apply(v, look);
            return true;
        }
        if (_buildsLeft <= 0) return false;
        if (_props.TryModel(v.ModelPath!, multimesh: false, out var mesh) == AssetState.Pending) return false;
        _buildsLeft--;
        look = mesh == null ? null : LookOf(mesh, v.Item);
        _looks[v.Item] = look;
        v.Awaiting = false;
        if (look == null) return false;
        Apply(v, look);
        return true;
    }

    private static Look LookOf(Mesh mesh, ItemDef item)
    {
        var box = mesh.GetAabb();
        float extent = Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
        float scale = (item.MaxStack > 1 || item.Pickup != null ? StackSize : GearSize) / Mathf.Max(extent, 0.01f);
        return new Look(mesh, VisualKind.Model, scale, -box.GetCenter() * scale, box.Size.Y * scale * 0.5f);
    }

    private void Apply(Visual v, Look look)
    {
        v.Look = look;
        v.Kind = look.Kind;
        v.Body.Mesh = look.Mesh;
        v.Body.Visible = true;
        v.Card.Visible = false;
    }

    private void UseCard(Visual v)
    {
        v.Look = null;
        v.Kind = VisualKind.Billboard;
        v.Card.MaterialOverride = MaterialFor(v.Item);
        v.Card.SetInstanceShaderParameter(GlowParam, v.Item.Glows ? 0.6f : v.Item.Rarity > 0 ? 0.08f : 0f);
        v.Card.Visible = true;
        v.Body.Visible = false;
    }

    /// <summary>Halo (rarity ≥ 1, the animated Expert/Master tiers, or a glowing item) and light (held light or glow).</summary>
    private void SetupGlow(Visual v, ItemDef item)
    {
        v.HasHalo = item.Glows || item.Rarity >= 1 || item.Rarity <= -10;
        v.AnimatedHalo = !item.Glows && UI.UiTheme.IsAnimated(item.Rarity);
        v.HaloColor = item.Glows ? GlowColor(item) : UI.UiTheme.Rarity(item.Rarity, _time);
        v.HaloStrength = item.Glows ? 0.85f : 0.4f;
        v.Halo.Visible = v.HasHalo;
        v.Fire = item.HeldLight != null && !item.Glows;
        if (item.HeldLight is { Length: 3 } l)
        {
            int max = Math.Max(1, Math.Max(l[0], Math.Max(l[1], l[2])));
            v.LightColor = new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max);
            v.LightPeak = max / 15f;
        }
        else if (item.Glows)
        {
            v.LightColor = v.HaloColor;
            v.LightPeak = 0.45f;
        }
        else v.LightPeak = 0f;
    }

    /// <summary>A glowing item's colour: its held light, else a Heart's red or a Star's blue, else starlight.</summary>
    private static Color GlowColor(ItemDef item)
    {
        if (item.HeldLight is { Length: 3 } l)
        {
            int max = Math.Max(1, Math.Max(l[0], Math.Max(l[1], l[2])));
            return new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max);
        }
        if (item.Pickup is { HealLife: > 0 }) return new Color(1f, 0.25f, 0.35f);
        if (item.Pickup is { HealMana: > 0 }) return new Color(0.35f, 0.55f, 1f);
        return new Color(1f, 0.9f, 0.5f);
    }

    /// <summary>Spins and bobs a drop above where it lies; glowing and floating items hover higher and pulse.</summary>
    private void Place(Visual v, DroppedItem item)
    {
        var def = v.Item;
        var p = item.Position;
        bool floats = def.Glows || def.NoGravity;
        float pulse = floats ? 1f + 0.1f * Mathf.Sin(_time * 5f + item.Id) : 1f;
        float hover = (floats ? 0.16f : 0.06f) + (floats ? 0.07f : 0.04f) * Mathf.Sin(_time * 2.5f + item.Id);
        float ground = p.Y - 0.15f;
        if (v.Look is { } look)
        {
            var rot = new Basis(Vector3.Up, _time * Spin + item.Id * 0.7f);
            v.Center = new Vector3(p.X, ground + hover + look.HalfHeight * pulse, p.Z);
            v.Body.Transform = new Transform3D(rot * Basis.FromScale(Vector3.One * (look.Scale * pulse)), v.Center + rot * (look.Offset * pulse));
        }
        else
        {
            float size = (def.Category == ItemCategory.Block ? 0.3f : 0.38f) * pulse;
            var at = new Vector3(p.X, ground + hover, p.Z);
            v.Card.Transform = new Transform3D(Basis.FromScale(Vector3.One * size), at);
            v.Center = at + Vector3.Up * (size * 0.5f);
        }
        if (!v.HasHalo) return;
        if (v.AnimatedHalo) v.HaloColor = UI.UiTheme.Rarity(def.Rarity, _time);
        float strength = v.HaloStrength * (0.8f + 0.2f * Mathf.Sin(_time * 3f + item.Id));
        v.Halo.Transform = new Transform3D(Basis.FromScale(Vector3.One * ((floats ? 0.9f : 0.75f) * pulse)), v.Center);
        v.Halo.SetInstanceShaderParameter(HaloParam, new Color(v.HaloColor, strength));
    }

    private ShaderMaterial MaterialFor(ItemDef item)
    {
        if (_materials.TryGetValue(item.Id, out var m)) return m;
        m = new ShaderMaterial { Shader = SpriteShader };
        m.SetShaderParameter("icon", ItemIcons.Get(item));
        _materials[item.Id] = m;
        return m;
    }

    /// <summary>
    /// Light candidates for DynamicLights (refreshed at 4 Hz): the nearest drops with a held light or a glow, at most
    /// the preset's <c>dropLights</c>; each follows its drop.
    /// </summary>
    public IEnumerable<DynamicLights.Source> Lights(Vector3 eye, float radius)
    {
        _lit.Clear();
        if (_lightBudget <= 0) yield break;
        foreach (var v in _visuals.Values)
            if (v.LightPeak > 0f && v.Center.DistanceTo(eye) <= radius) _lit.Add(v);
        _eye = eye;
        _lit.Sort(_byDistance ??= (a, b) => a.Center.DistanceSquaredTo(_eye).CompareTo(b.Center.DistanceSquaredTo(_eye)));
        for (int i = 0; i < Math.Min(_lightBudget, _lit.Count); i++)
        {
            var v = _lit[i];
            yield return new DynamicLights.Source(v.Center, v.LightColor, v.LightPeak, v.Fire ? DynamicLights.Kind.Fire : DynamicLights.Kind.Steady,
                2.5f + v.LightPeak * 3.5f, 0.35f + v.LightPeak * 0.6f, (i * 0.618034f) % 1f, v.Track);
        }
    }
}
