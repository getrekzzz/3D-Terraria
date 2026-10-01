using System;
using Godot;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// A dynamic light carried by the player: the held item's <c>heldLight</c> (torches, glowing blocks) or the brightest
/// light-emitting effect (Radiance and similar buffs). It shines from the torch in the hand (first or third person)
/// with a fire's irregular flicker, brighter and further than a placed torch's pooled light so it lights the way ahead
/// (data/config/lighting.json <c>torch.held*</c>). Placed torches are baked into voxel light plus
/// <see cref="Rendering.DynamicLights"/>.
/// </summary>
public sealed partial class PlayerLight : OmniLight3D
{
    /// <summary>Fallback position relative to the player when nothing is held in view (buff light).</summary>
    private static readonly Vector3 BodyOffset = new(0.35f, 1.5f, 0.25f);

    private PlayerEntity _entity = null!;
    private Func<Node3D?>[] _anchors = Array.Empty<Func<Node3D?>>();
    private Strata.Core.Presentation.TorchSettings _torch = null!;
    private float _time;

    public void Initialize(PlayerEntity entity, Rendering.GraphicsSettings graphics, params Func<Node3D?>[] anchors)
    {
        _entity = entity;
        _anchors = anchors;
        _torch = graphics.Lighting.Torch;
        Name = "PlayerLight";
        TopLevel = true; // positioned in world space at the hand
        ShadowEnabled = graphics.Preset.TorchShadows;
        ShadowBias = 0.08f;
        ShadowNormalBias = 1.5f;
        LightSize = 0.08f; // soft torch shadows
        OmniAttenuation = _torch.HeldAttenuation; // flatter than inverse-square: a torch lights the cave, not just the wall beside it
        LightVolumetricFogEnergy = 1.5f;
        graphics.Changed += p => ShadowEnabled = p.TorchShadows;
        Visible = false;
    }

    /// <summary>The carried light's colour in the voxel-light encoding (0–1 per channel), or null.</summary>
    public static int[]? Carried(PlayerEntity entity)
    {
        var held = entity.State.Inventory.Held;
        int[]? rgb = !held.IsEmpty && held.Item!.HeldLight is { Length: 3 } h ? h : null;
        var effect = entity.State.Effects.Light;
        if (effect != null && (rgb == null || Sum(effect) > Sum(rgb))) rgb = effect;
        return entity.Alive ? rgb : null;
    }

    /// <summary>A light probe value brightened by what the player carries (their own torch lights them).</summary>
    public static Color Brighten(Color probe, PlayerEntity entity)
    {
        if (Carried(entity) is not { } c) return probe;
        return new Color(Mathf.Max(probe.R, c[0] / 15f), Mathf.Max(probe.G, c[1] / 15f), Mathf.Max(probe.B, c[2] / 15f), probe.A);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.player_light");
        _time += (float)delta;
        var held = _entity.State.Inventory.Held;
        var rgb = Carried(_entity);
        Visible = rgb != null;
        if (!Visible) return;
        float peak = Mathf.Max(rgb![0], Mathf.Max(rgb[1], rgb[2])) / 15f;
        LightColor = new Color(rgb[0] / 15f, rgb[1] / 15f, rgb[2] / 15f) / Mathf.Max(peak, 0.01f);
        OmniRange = _torch.HeldRangeBase + peak * _torch.HeldRangePerPeak;
        bool fire = !held.IsEmpty && held.Item!.HeldLight != null;
        float t = _time;
        // A fire's flicker: several unrelated rhythms plus the odd gutter, never a clean sine.
        float flicker = !fire ? 1f : 1f + Mathf.Sin(t * 17.3f) * 0.035f + Mathf.Sin(t * 7.1f + 1.3f) * 0.04f
            + Mathf.Sin(t * 23.9f + 0.7f) * 0.025f + Mathf.Sin(t * 2.3f) * 0.03f - Mathf.Max(0f, Mathf.Sin(t * 0.91f) - 0.93f) * 1.5f;
        LightEnergy = _torch.HeldEnergy * peak * flicker;
        var anchor = Anchor();
        var parent = GetParent<Node3D>();
        var at = anchor != null ? anchor.GlobalPosition + Vector3.Up * 0.12f : parent.GlobalTransform * BodyOffset;
        // The flame's light wanders by a centimetre or two as it licks (not while it casts shadows: every move of a
        // shadowed light re-renders its shadow map).
        if (fire && !ShadowEnabled) at += new Vector3(Mathf.Sin(t * 11.3f), Mathf.Sin(t * 13.7f + 2f) * 0.5f, Mathf.Sin(t * 9.1f + 4f)) * 0.012f;
        GlobalPosition = at;
    }

    private Node3D? Anchor()
    {
        foreach (var get in _anchors)
            if (get() is { } node && IsInstanceValid(node) && node.IsVisibleInTree()) return node;
        return null;
    }

    private static int Sum(int[] v) => v[0] + v[1] + v[2];
}
