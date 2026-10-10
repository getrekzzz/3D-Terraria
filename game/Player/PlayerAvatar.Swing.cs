using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// What is seen is what hits (P3-04), in third person: a swung blade is scaled so its tip reaches as far as the hit
/// (<see cref="SwingPlane.SwingReach"/> past the arm, clamped 0.75–1.6 of the modelled size), and spears and
/// shortswords slide out of the fist along their shaft by however much further the thrust reaches than the model,
/// times the thrust's <see cref="SwingCurve.Extension"/> at this frame's use phase.
/// </summary>
public sealed partial class PlayerAvatar
{
    private Transform3D _heldRest;
    private float _thrustOut;
    private SwingCurve? _extension;

    /// <summary>How far (m) the modelled blade reaches from the grip: its tip socket, else its mesh's furthest +Y.</summary>
    public static float ModelLength(PropLibrary props, string path)
    {
        if (props.Socket(path, "tip") is { } tip) return Mathf.Max(0.05f, tip.Origin.Y);
        var box = props.Bounds(path);
        return Mathf.Max(0.05f, box.Position.Y + box.Size.Y);
    }

    /// <summary>The scale that brings a swung blade's tip to its hit reach (1 for anything else).</summary>
    public static float MeleeScale(GameWorld game, ItemUseInfo info, PropLibrary props, string path, float size)
    {
        if (ActionFor(game, info) != UseTiming.Swing || info.Swing == null) return 1f;
        float blade = SwingPlane.SwingReach(info.Swing.Length, size) - SwingPlane.SwingArm;
        return Mathf.Clamp(blade / ModelLength(props, path), 0.75f, 1.6f);
    }

    /// <summary>How much further (m) a thrust reaches than the (scaled) model: the slide at full extension.</summary>
    public static float ThrustSlide(GameWorld game, ItemUseInfo info, PropLibrary props, string? path, float scale, float size)
    {
        if (path == null || ActionFor(game, info) is not (UseTiming.Rapier or UseTiming.Spear) || info.ThrustParams == null) return 0f;
        float reach = SwingPlane.ThrustReach(info.ThrustParams.Length, size) - SwingPlane.ThrustArm;
        return Mathf.Max(0f, reach - ModelLength(props, path) * scale);
    }

    /// <summary>Remembers the held node's rest pose and its thrust slide (UpdateHeld).</summary>
    private void RememberHeld(Node3D node, ItemDef item, string? path, float scale, float size)
    {
        _heldRest = node.Transform;
        var info = _game.Uses[item];
        _thrustOut = ThrustSlide(_game, info, _props, path, scale, size);
        _extension = _thrustOut > 0f ? SwingCurve.Extension(_game.ItemUse.Timing.Clip(_game.ItemUse.Timing.ThrustClip(info))) : null;
    }

    /// <summary>Slides a held spear or shortsword out along its shaft as the thrust extends.</summary>
    private void ExtendThrust(PlayerEntity entity)
    {
        if (_held == null || _extension == null || !IsInstanceValid(_held)) return;
        float ext = 0f;
        if (entity.Alive && entity.UseAnimation > 0 && entity.UseItem == _heldItem)
        {
            float frac = _controller.Runtime.Simulating ? (float)Engine.GetPhysicsInterpolationFraction() : 0f;
            int max = Mathf.Max(1, entity.UseAnimationMax);
            ext = _extension.Evaluate(Mathf.Clamp((max - entity.UseAnimation + frac) / max, 0f, 1f));
        }
        // The slide is in metres; the hand bone's space may be scaled, so convert.
        float perLocal = _held.GetParentNode3D() is { } parent ? Mathf.Max(1e-3f, parent.GlobalBasis.Y.Length()) : 1f;
        var axis = _heldRest.Basis.Y.Normalized();
        _held.Transform = new Transform3D(_heldRest.Basis, _heldRest.Origin + axis * (_thrustOut * ext / perLocal));
    }

    /// <summary>The size prefix of the stack in hand (1 without one).</summary>
    private static float HeldSize(ItemStack held) => Prefixes.Size(held);
}
