using Godot;
using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Game.World;

namespace Strata.Game.Player;

public enum HookState
{
    None,
    Flying,
    Attached,
    Retracting,
}

/// <summary>
/// Grappling hooks: the grapple key throws the best hook the player carries (equipped first, then the inventory) along
/// the aim; it latches onto the first block within reach and reels the player in, holding them there. Jumping,
/// pressing the key again or the block breaking lets go.
/// </summary>
public sealed partial class PlayerController
{
    private GrappleParams? _hook;
    private Vector3 _hookTarget;
    private bool _hookHit;

    public HookState Hook { get; private set; }

    /// <summary>Where the hook head is (world metres).</summary>
    public Vector3 HookHead { get; private set; }

    /// <summary>The chain colour of the hook in use.</summary>
    public Color HookColor => Color.FromHtml(_hook?.Color ?? "#9a9aa4");

    public Vector3 HandPosition => GlobalPosition + Vector3.Up * 1.25f + Rig.YawBasis * new Vector3(0.28f, 0, -0.1f);

    /// <summary>The hook the player would throw: equipped accessories first, then the hotbar and backpack.</summary>
    public GrappleParams? BestHook()
    {
        var inv = Entity.State.Inventory;
        var uses = _runtime.Game.Uses;
        GrappleParams? best = null;
        foreach (var container in new[] { inv.Accessories, inv.Backpack })
            for (int i = 0; i < container.Size; i++)
            {
                var s = container[i];
                if (s.IsEmpty || uses[s.Item!].Grapple is not { } g) continue;
                if (best == null || g.Reach > best.Reach) best = g;
            }
        return best;
    }

    private void FireHook()
    {
        _hook = BestHook();
        if (_hook == null) return;
        float reach = _hook.Reach * Units.CellSize;
        var hand = HandPosition;
        var from = Rig.Camera.GlobalPosition;
        var dir = Rig.Forward;
        float camOffset = from.DistanceTo(hand);
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * (reach + camOffset), ColliderManager.TerrainLayer | ColliderManager.ObjectLayer);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = space.IntersectRay(query);
        _hookHit = hit.Count > 0 && ((Vector3)hit["position"]).DistanceTo(hand) <= reach;
        _hookTarget = _hookHit ? (Vector3)hit["position"] : hand + (hit.Count > 0 ? ((Vector3)hit["position"] - hand).Normalized() : dir) * reach;
        HookHead = hand;
        Hook = HookState.Flying;
        HookFired?.Invoke();
    }

    /// <summary>Raised when a hook is thrown (sound).</summary>
    public event System.Action? HookFired;

    public void ReleaseHook()
    {
        if (Hook == HookState.None) return;
        Hook = HookState.Retracting;
    }

    /// <summary>Advances the hook; while latched it drives the velocity and returns true.</summary>
    private bool UpdateGrapple(float dt, ref Vector3 v)
    {
        if (!UI.InputGate.GameplayBlocked && Input.IsActionJustPressed("grapple"))
        {
            if (Hook is HookState.None or HookState.Retracting) FireHook();
            else ReleaseHook();
        }
        switch (Hook)
        {
            case HookState.Flying:
            {
                HookHead = HookHead.MoveToward(_hookTarget, _hook!.Speed * dt);
                if (HookHead.DistanceTo(_hookTarget) < 0.01f) Hook = _hookHit ? HookState.Attached : HookState.Retracting;
                return false;
            }
            case HookState.Retracting:
                HookHead = HookHead.MoveToward(HandPosition, (_hook?.Speed ?? 30f) * 1.5f * dt);
                if (HookHead.DistanceTo(HandPosition) < 0.2f) Hook = HookState.None;
                return false;
            case HookState.Attached:
            {
                // Let go when the block holding the hook is gone.
                var anchor = CellAt(_hookTarget + (_hookTarget - HandPosition).Normalized() * 0.05f);
                if (!_runtime.Session.World.IsSolid(anchor.X, anchor.Y, anchor.Z) && _runtime.Session.World.TileObjects.At(anchor) == null)
                {
                    ReleaseHook();
                    return false;
                }
                if (!UI.InputGate.GameplayBlocked && Input.IsActionJustPressed("jump"))
                {
                    ReleaseHook();
                    v.Y = Mathf.Max(v.Y, _config.JumpSpeed);
                    return false;
                }
                var body = GlobalPosition + Vector3.Up * 0.9f;
                var to = _hookTarget - body;
                float dist = to.Length();
                v = dist > 1.1f ? to / dist * _hook!.Pull : Vector3.Zero;
                _peakY = GlobalPosition.Y;
                RefillAirMovement(); // hanging on a hook counts as a fresh start for jumps and wings
                return true;
            }
            default:
                return false;
        }
    }
}
