using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// Ranged weapons in first person, held aimed whether or not they are in use. Their poses are in the aim frame: the
/// view turned so that −Z runs from the item to the crosshair point, so a barrel or an arrow laid along −Z points at
/// what the crosshair is on at any distance. A bow stands upright right of centre with its back to the target and an
/// arrow on the string (the first arrows in the ammo slots or backpack, as the shot will use): the arrow leaves at the
/// clip's release as the bow jolts forward, and the next one is laid on and slid back onto the string through the
/// re-draw window (data/config/use_styles.json, the body's bow clip), so auto-fire stays in step with the shots. The
/// rest poses (in the aim frame), the casts' push and every shot's recoil are the styles' <c>fp</c> data
/// (ViewModel.Poses): guns point along the view, staffs, wands and tomes lean their tip toward the crosshair.
/// </summary>
public sealed partial class ViewModel
{
    // Body clips (data/config/use_styles.json) the view model poses by: guns and generic shooters, staffs, tomes, and
    // the throws and whips it raises like a swing.
    private const string Gun = "gun", Staff = "staff", Tome = "tome", Throw = "throw", Whip = "whip";

    /// <summary>The crosshair point the aim frame looks at is kept this far (m) from the eye, so close walls do not twist the weapon.</summary>
    private const float MinAimDistance = 2f, MaxAimDistance = 60f;

    // The nocked arrow in the bow's frame: its nock on the string (the bow model's origin) just above the grip and
    // beside the riser, pointing along the bow's back. The arrow model's nock is 0.35 m behind its origin (bgen
    // items.arrow); a new arrow is laid on this far ahead of the string, then slid back onto it.
    private static readonly Vector3 Nock = new(0f, 0.03f, 0.035f);
    private const float ArrowNock = 0.35f, NockSlide = 0.22f;

    /// <summary>How often (s) the backpack is checked for the arrow to show on the string.</summary>
    private const float AmmoRefresh = 0.25f;

    private MeshInstance3D? _arrow;
    private ItemDef? _ammo;
    private Mesh? _ammoMesh;
    private float _ammoClock, _kick;

    /// <summary>First person: 1 while the bow shows an arrow on (or sliding onto) the string, 0 while it is off, −1 without a bow and arrows.</summary>
    public int ArrowState => _arrow == null || _arrow.Mesh == null ? -1 : _arrow.Visible ? 1 : 0;

    /// <summary>
    /// First person: how far (degrees) a held bow's arrow or gun's barrel points from the crosshair point; −1 for
    /// anything else or out of view. About 0 at rest; a fresh shot's recoil tips it up to 6.
    /// </summary>
    public float AimErrorDegrees
    {
        get
        {
            if (_style is not (UseTiming.Bow or Gun) || _model == null || !IsVisibleInTree()) return -1f;
            var eye = _controller.Rig.Camera.GlobalTransform;
            var target = eye.Origin - eye.Basis.Z * Mathf.Clamp(_controller.Entity.AimDistance, MinAimDistance, MaxAimDistance);
            var b = GlobalBasis;
            var axis = _style == UseTiming.Bow ? -b.X : b.Y;
            return Mathf.RadToDeg(axis.AngleTo(target - GlobalPosition));
        }
    }

    /// <summary>
    /// Places a ranged weapon: its pose turned into the aim frame (yawed and pitched from the item's rest position to
    /// the crosshair point), plus the walking bob and the latest shot's recoil.
    /// </summary>
    private void PlaceAimed(PlayerEntity entity, Pose pose, Vector3 bob, float dt)
    {
        _kick *= Mathf.Exp(-_recoilRate * dt);
        var at = Rest + pose.Offset;
        float ahead = Mathf.Clamp(entity.AimDistance, MinAimDistance, MaxAimDistance) + at.Z;
        float yaw = Mathf.Atan2(at.X, ahead);
        float pitch = Mathf.Atan2(-at.Y, new Vector2(at.X, ahead).Length()) + _recoil.Rotation.X * _kick;
        Position = at + bob + _recoil.Offset * _kick;
        Basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch) * Basis.FromEuler(pose.Rotation);
    }

    private void AddArrow(MeshInstance3D bow)
    {
        _arrow = new MeshInstance3D
        {
            Name = "NockedArrow", Mesh = _ammoMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Basis = new Basis(Vector3.Back, Mathf.Pi / 2f), // the arrow's tip (+Y) along the bow's back (−X)
        };
        bow.AddChild(_arrow);
        UpdateArrow();
    }

    /// <summary>
    /// The arrow a held bow will shoot (the first arrows in the ammo slots, then the backpack), looked up at most four
    /// times a second and at once when a bow is taken out, in either view, so its model is loaded before the first shot.
    /// </summary>
    private void RefreshAmmo(PlayerEntity entity, float dt)
    {
        if (_style != UseTiming.Bow || _item?.UseAmmo is not { } ammoClass)
        {
            _ammo = null;
            _ammoMesh = null;
            return;
        }
        if ((_ammoClock -= dt) > 0f) return;
        _ammoClock = AmmoRefresh;
        var ammo = entity.State.Inventory.FindAmmo(ammoClass) is { } found ? found.Inv[found.Slot].Item : null;
        if (ammo == _ammo) return;
        _ammo = ammo;
        var path = ammo == null ? null : AssetPaths.ItemModel(ammo, _game.Session.Content);
        _ammoMesh = path == null ? null : _props.Model(path, multimesh: false);
        if (_arrow != null) _arrow.Mesh = _ammoMesh;
    }

    /// <summary>
    /// The nocked arrow at the use's phase: on the string until the release, gone until the re-draw start, then laid on
    /// ahead of the string and slid back onto it by the re-draw end. On the string between uses.
    /// </summary>
    private void UpdateArrow()
    {
        if (_arrow == null) return;
        float t = _engage > 0f && _action == UseTiming.Bow ? _phase : 1f;
        var clip = _game.ItemUse.Timing.Clip(UseTiming.Bow);
        float release = clip.Release ?? 0f, from = clip.RedrawStart ?? release, to = clip.RedrawEnd ?? from;
        float slide = t < release || t >= to ? 0f : t < from ? -1f : NockSlide * (1f - Ease((t - from) / (to - from)));
        _arrow.Visible = _arrow.Mesh != null && slide >= 0f;
        _arrow.Position = Nock - new Vector3(ArrowNock + Mathf.Max(slide, 0f), 0f, 0f);
    }
}
