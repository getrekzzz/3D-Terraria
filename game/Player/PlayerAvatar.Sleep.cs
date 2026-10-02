using Godot;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// Asleep in a bed (P3-34): the body plays the "sleep" rest pose (on the back, hips on the mattress) turned so the
/// head lies towards the headboard, the torso no longer follows the camera, the held item is hidden (ShowHeld), and
/// a soft puff rises now and then.
/// </summary>
public sealed partial class PlayerAvatar
{
    private const float SnorePuffSeconds = 2.4f;
    private float _snore;

    private void UpdateSleep(PlayerEntity entity, HumanoidAnimator h, float dt)
    {
        ShowHeld(entity); // hidden in bed, and while a yoyo, flail or whip is out (PlayerAvatar.Held)
        if (entity.Sleeping) LieInBed(entity, h, dt);
    }

    private void LieInBed(PlayerEntity entity, HumanoidAnimator h, float dt)
    {
        var (_, head) = GameWorld.BedRest(entity.SleepBed!);
        _legYaw = Mathf.Atan2(head.X, head.Z); // the pose lays the head along the model's back (+Z)
        Rotation = new Vector3(0, _legYaw, 0);
        if (h.Aim is { } aim) { aim.Twist = 0f; aim.Pitch = 0f; aim.ArmAim = RestArmAim; }
        if ((_snore += dt) < SnorePuffSeconds) return;
        _snore = 0f;
        var at = GlobalPosition + new Vector3(head.X, 0, head.Z) * 0.75f + Vector3.Up * 0.5f;
        EffectKit.Instance?.Burst(EffectRecipe.Puff, at, new Color(0.85f, 0.88f, 1f, 0.5f), 1, 0.4f, Vector3.Up, 0.1f);
    }
}
