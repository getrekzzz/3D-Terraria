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

    public void Initialize(PlayerEntity entity)
    {
        _entity = entity;
        Name = "PlayerLight";
        ShadowEnabled = false;
        Position = new Vector3(0.35f, 1.5f, 0.25f);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        var held = _entity.State.Inventory.Held;
        int[]? rgb = !held.IsEmpty && held.Item!.HeldLight is { Length: 3 } h ? h : null;
        var effect = _entity.State.Effects.Light;
        if (effect != null && (rgb == null || Sum(effect) > Sum(rgb))) rgb = effect;
        Visible = rgb != null && _entity.Alive;
        if (!Visible) return;
        float peak = Mathf.Max(rgb![0], Mathf.Max(rgb[1], rgb[2])) / 15f;
        LightColor = new Color(rgb[0] / 15f, rgb[1] / 15f, rgb[2] / 15f) / Mathf.Max(peak, 0.01f);
        OmniRange = 3f + peak * 7f;
        float flicker = held.IsEmpty ? 1f : 1f + Mathf.Sin(_time * 17f) * 0.04f + Mathf.Sin(_time * 7.3f) * 0.05f;
        LightEnergy = 1.3f * peak * flicker;
    }

    private static int Sum(int[] v) => v[0] + v[1] + v[2];
}
