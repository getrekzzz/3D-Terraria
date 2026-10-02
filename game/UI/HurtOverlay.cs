using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Player;

namespace Strata.Game.UI;

/// <summary>
/// The hurt flash: the screen edges go red when the local player is hurt, by the share of life lost (strong in first
/// person, faint in third), fading over 0.35 s; real hits (with a push) also punch the camera. Off with the
/// <c>HurtFlash</c> setting (the punch follows camera shake).
/// </summary>
public sealed partial class HurtOverlay : CanvasLayer
{
    private const float FadeTime = 0.35f;
    private static readonly StringName StrengthParam = "strength";

    private ColorRect _rect = null!;
    private ShaderMaterial _material = null!;
    private CameraRig _rig = null!;
    private PlayerEntity _local = null!;
    private float _strength;

    /// <summary>Hurt flashes so far (probe <c>hud.hurt_flashes</c>).</summary>
    public int Flashes { get; private set; }

    public void Initialize(GameWorld game, PlayerEntity local, CameraRig rig)
    {
        Name = "HurtOverlay";
        Layer = 8; // under the HUD (10)
        _local = local;
        _rig = rig;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hurt_vignette.gdshader") };
        _rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        game.Npcs.PlayerHurt += OnHurt;
    }

    private void OnHurt(PlayerEntity p, int damage, System.Numerics.Vector3 knock)
    {
        if (p != _local || damage <= 0) return;
        float share = Mathf.Clamp(4f * damage / Mathf.Max(1, p.State.Vitals.MaxLife), 0f, 1f);
        if (knock.LengthSquared() > 1e-4f) CameraShake.Punch(Mathf.Lerp(0.1f, 0.35f, share));
        if (!Settings.UserSettings.Current.HurtFlash) return;
        Flashes++;
        _strength = Mathf.Max(_strength, (_rig.FirstPerson ? 0.35f : 0.15f) * share);
    }

    public override void _Process(double delta)
    {
        if (_strength <= 0f)
        {
            if (_rect.Visible) _rect.Visible = false;
            return;
        }
        _rect.Visible = true;
        _material.SetShaderParameter(StrengthParam, _strength);
        _strength = Mathf.Max(0f, _strength - (float)delta / FadeTime * 0.35f);
    }
}
