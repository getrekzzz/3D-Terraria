using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Game.Items;
using Strata.Game.Rendering;

namespace Strata.Game.Player;

/// <summary>
/// How the held item is carried and how a blade reads as it strikes: between uses the right hand tips blades and tools
/// forward and down beside the leg (staffs, spears, bows and rods up) instead of holding everything straight out; during
/// a swing, a rapier's jab, a spear's lunge or a chop the blade itself leaves a streak tinted to the item, timed by the
/// clip's strike window (data/config/use_styles.json), over the hit ribbon the melee system draws (Entities/SwingTrail).
/// </summary>
public sealed partial class PlayerAvatar
{
    private BladeTrail? _trail;
    private Vector2 _carry;
    private float _reach; // how far the held item reaches past the fist (m)

    private void InitializeBlade()
    {
        if (_visual?.Skeleton is not { } skeleton || _visual.Humanoid == null) return;
        _trail = new BladeTrail();
        AddChild(_trail);
        _trail.Initialize(skeleton);
    }

    /// <summary>Points the trail at a newly held item: from about a third of the way up to its tip.</summary>
    private void TrackBlade(Node3D? node, ItemDef? item)
    {
        _reach = node is MeshInstance3D { Mesh: { } m } ? m.GetAabb().End.Y * node.Scale.Y : 0.5f;
        if (_trail == null) return;
        if (node is not MeshInstance3D { Mesh: { } mesh } || item == null)
        {
            _trail.Track(null, Vector3.Zero, Vector3.Zero, Colors.White);
            return;
        }
        var box = mesh.GetAabb();
        float top = box.End.Y, bottom = Mathf.Max(0f, box.Position.Y);
        var centre = box.GetCenter();
        bool blade = top - bottom > 0.15f; // (short items leave no trail)
        _trail.Track(blade ? node : null, new Vector3(centre.X, Mathf.Lerp(bottom, top, 0.3f), centre.Z),
            new Vector3(centre.X, top, centre.Z), blade ? Tint(item) : Colors.White);
    }

    /// <summary>Called every frame after the animation is chosen: the carry and the trail.</summary>
    private void AnimateBlade(HumanoidAnimator h, AimModifier aim, bool alive, Color light, float dt)
    {
        var info = _heldItem != null ? _game.Uses[_heldItem] : null;
        // (Not while the hands are busy climbing, swimming or gliding, the body rests, or the right arm holds a loop.)
        bool free = alive && h.Action == null && h.HoldIndex < 0
            && h.State is not ("climb" or "swim" or "glide" or "sit" or "sleep" or "bound" or "lie");
        var target = free && _heldItem != null && info != null && _heldItem.HeldLight == null
            ? CarryFor(info, _reach) : Vector2.Zero;
        // Out of the carry as fast as an action fades in, back into it as it fades out.
        _carry = _carry.MoveToward(target, dt * (target == Vector2.Zero ? 700f : 240f));
        aim.Carry = new Vector2(Mathf.DegToRad(_carry.X), Mathf.DegToRad(_carry.Y));
        if (_trail == null) return;
        _trail.Strength = alive ? TrailStrength(h.Action, h.ActionPhase) : 0f;
        _trail.Life = Mathf.Clamp(h.ActionLength * 0.3f, 0.07f, 0.16f);
        _trail.SetLight(Mathf.Max(light.R, Mathf.Max(light.G, light.B)), light.A);
    }

    /// <summary>
    /// The carry (degrees about the hand's X and its length, see <see cref="AimModifier.Carry"/>): blades and tools
    /// forward and down, fanned out from the leg, long ones less steeply so the tip stays off the ground; spears, staffs,
    /// bows and rods up; guns and the rest as the grip holds them.
    /// </summary>
    private Vector2 CarryFor(ItemUseInfo info, float reach)
    {
        if (info.Has(ItemAction.Fish)) return new Vector2(25f, 0f);
        return ActionFor(_game, info) switch
        {
            "swing" or "mine" or "rapier" => new Vector2(Mathf.Lerp(-30f, -18f, Mathf.Clamp((reach - 0.6f) / 0.5f, 0f, 1f)), 15f),
            "spear" or "staff" or "bow" => new Vector2(22f, 0f),
            _ => Vector2.Zero,
        };
    }

    /// <summary>
    /// How strongly the blade trails at this point of an action: while a swing strikes and follows through (the window
    /// the melee system sweeps hits in), as a rapier or spear drives out, and faintly as a tool comes down.
    /// </summary>
    private float TrailStrength(string? action, float phase)
    {
        float strength = action switch { "swing" => 1f, "rapier" or "spear" => 0.8f, "mine" => 0.3f, _ => 0f };
        if (strength <= 0f) return 0f;
        var clip = _game.ItemUse.Timing.Clip(action!);
        float start = clip.StrikeStart ?? 0f, end = Mathf.Min(1f, (clip.StrikeEnd ?? 1f) + UseTiming.FollowThrough);
        return strength * Window(phase, start, end);
    }

    private static float Window(float t, float start, float end) =>
        Mathf.SmoothStep(start - 0.03f, start + 0.02f, t) * (1f - Mathf.SmoothStep(end - 0.04f, end + 0.02f, t));

    private static readonly Dictionary<string, Color> Tints = new();

    /// <summary>An item's trail colour: its icon's average colour, mostly washed toward white (worked out once per item:
    /// reading the icon back from the GPU stalls).</summary>
    private static Color Tint(ItemDef item)
    {
        if (!Tints.TryGetValue(item.Id, out var tint)) Tints[item.Id] = tint = AverageTint(item);
        return tint;
    }

    private static Color AverageTint(ItemDef item)
    {
        var image = ItemIcons.Get(item)?.GetImage();
        if (image == null || image.IsEmpty()) return Colors.White;
        image = (Image)image.Duplicate();
        if (image.IsCompressed()) image.Decompress();
        image.Resize(16, 16, Image.Interpolation.Bilinear);
        float r = 0, g = 0, b = 0, weight = 0;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var c = image.GetPixel(x, y);
                r += c.R * c.A;
                g += c.G * c.A;
                b += c.B * c.A;
                weight += c.A;
            }
        if (weight < 1e-3f) return Colors.White;
        var mean = new Color(r / weight, g / weight, b / weight);
        float peak = Mathf.Max(mean.R, Mathf.Max(mean.G, mean.B));
        return peak < 1e-3f ? Colors.White : Colors.White.Lerp(mean / peak, 0.45f);
    }
}
