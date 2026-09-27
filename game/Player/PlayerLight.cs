using Godot;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// A dynamic light carried by the player: the held item's <c>heldLight</c> (torches, glowing blocks) or the brightest
/// light-emitting effect (Radiance and similar buffs). Placed torches are baked into voxel light instead.
/// </summary>
public sealed partial class PlayerLight : OmniLight3D
{
    private PlayerEntity _entity = null!;
    private float _time;

    public void Initialize(PlayerEntity entity, Rendering.GraphicsSettings graphics)
    {
        _entity = entity;
        Name = "PlayerLight";
        ShadowEnabled = graphics.Preset.TorchShadows;
        ShadowBias = 0.08f;
        ShadowNormalBias = 1.5f;
        LightSize = 0.08f; // soft torch shadows
        graphics.Changed += p => ShadowEnabled = p.TorchShadows;
        Position = new Vector3(0.35f, 1.5f, 0.25f);
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
        OmniRange = 3f + peak * 7f;
        float flicker = held.IsEmpty ? 1f : 1f + Mathf.Sin(_time * 17f) * 0.04f + Mathf.Sin(_time * 7.3f) * 0.05f;
        LightEnergy = 1.3f * peak * flicker;
    }

    private static int Sum(int[] v) => v[0] + v[1] + v[2];
}
