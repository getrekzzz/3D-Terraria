using Godot;
using Strata.Core.Gameplay.Player;

namespace Strata.Game.Player;

/// <summary>
/// Grappling hooks on the body. The Core <see cref="GrappleHook"/> on the entity decides where a throw latches, when it
/// lets go and which way it pulls; the body advances it each physics step and applies the pull. The grapple key throws
/// the best hook the player carries (equipped first, then the inventory) or lets go of one already out; clicking with a
/// hook held does the same through the item use. Jumping, pressing again or the anchor cell breaking lets go.
/// </summary>
public sealed partial class PlayerController
{
    public HookState Hook => Entity.Hook.State;

    /// <summary>Where the hook head is (world metres).</summary>
    public Vector3 HookHead => ToGodot(Entity.Hook.Head);

    /// <summary>The chain colour of the hook in use.</summary>
    public Color HookColor => Color.FromHtml(Entity.Hook.Params?.Color ?? "#9a9aa4");

    /// <summary>The throwing hand (world metres) at this physics step: hooks leave from and come back to it.</summary>
    public Vector3 HandPosition => GlobalPosition + HandOffset;

    /// <summary>The hand where the body is drawn this frame (between physics steps); the chain hangs from it.</summary>
    public Vector3 DrawnHandPosition => GetGlobalTransformInterpolated().Origin + HandOffset;

    private Vector3 HandOffset => Vector3.Up * 1.25f + Rig.YawBasis * new Vector3(0.28f, 0, -0.1f);

    /// <summary>Advances the hook; while latched it drives the velocity and returns true.</summary>
    private bool UpdateGrapple(float dt, ref Vector3 v)
    {
        var hook = Entity.Hook;
        var world = _runtime.Session.World;
        var hand = HandPosition;
        var coreHand = new System.Numerics.Vector3(hand.X, hand.Y, hand.Z);
        if (!UI.InputGate.MovementBlocked && Input.IsActionJustPressed("grapple"))
        {
            if (hook.State is HookState.Flying or HookState.Attached) hook.Release();
            else if (GrappleHook.Best(Entity.State, _runtime.Game.Uses, out var item) is { } best)
                hook.Fire(world, best, item!, coreHand, Entity.Eye, Entity.Aim);
        }
        hook.Tick(world, dt, coreHand);
        if (hook.State != HookState.Attached) return false;
        if (!UI.InputGate.MovementBlocked && Input.IsActionJustPressed("jump"))
        {
            hook.Release();
            v.Y = Mathf.Max(v.Y, _config.JumpSpeed);
            return false;
        }
        var body = GlobalPosition + Vector3.Up * 0.9f;
        v = ToGodot(hook.PullVelocity(new System.Numerics.Vector3(body.X, body.Y, body.Z)));
        _peakY = GlobalPosition.Y;
        RefillAirMovement(); // hanging on a hook counts as a fresh start for jumps and wings
        return true;
    }

    /// <summary>
    /// Riding a minecart or phasing through blocks: a hook that is out lets go and comes back to the hand, instead of
    /// hanging frozen in the air and yanking the body back to its anchor on the way out.
    /// </summary>
    private void ReelInHook(float dt)
    {
        var hook = Entity.Hook;
        if (!hook.Active) return;
        hook.Release();
        var hand = HandPosition;
        hook.Tick(_runtime.Session.World, dt, new System.Numerics.Vector3(hand.X, hand.Y, hand.Z));
    }
}
