using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's pickup popups (<see cref="PickupFeed"/>): what the player collects floats up from their head as
/// "Wood (37)" in its rarity colour (the Expert and Master tiers cycle every frame), a potion a quick key drinks as
/// "Used Healing Potion", and coins as one line in the colour of their largest coin ("1 gold 23 silver"). Picking up
/// more of the same while its popup is kept grows that popup with a little pop, so mining a vein shows one line. The
/// anchor is the point 0.9 m over the head, projected, in third person; in first person, or when that point is behind
/// the camera, a fixed point at (W/2, 0.38 H); kept inside the screen. Drawn straight onto this full-screen control
/// (no node or tween per popup); a popup's text is rebuilt only when its count changes, and nothing runs while none
/// is shown.
/// </summary>
public sealed partial class PickupPopups : Control
{
    private const int FontSize = 18;
    private const float HeadClearance = 0.9f;
    private const float FirstPersonHeight = 0.38f;
    private const float EdgeMargin = 24;
    private const float Follow = 14f; // per second: how fast a popup slides to its row when the stack reorders

    private readonly UiContext _ui;
    private readonly PickupFeed _feed = new();
    private readonly string[] _texts = new string[PickupFeed.Capacity];
    private readonly int[] _versions = new int[PickupFeed.Capacity];
    private readonly float[] _widths = new float[PickupFeed.Capacity];
    private readonly float[] _offsets = new float[PickupFeed.Capacity]; // pixels above the anchor, eased
    private readonly bool[] _wasShown = new bool[PickupFeed.Capacity];
    private Vector2 _anchor;
    private bool _active;

    public PickupPopups(UiContext ui)
    {
        _ui = ui;
        Name = "PickupPopups";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        ui.Game.ItemPickedUp += (p, stack) =>
        {
            if (p != ui.Entity || stack.IsEmpty) return;
            _feed.Add(stack);
            _active = true;
        };
        // A potion drunk with a quick key (H, J, B) floats as "Used Healing Potion".
        ui.Game.ItemUse.QuickUsed += (p, item) =>
        {
            if (p != ui.Entity) return;
            _feed.AddUsed(item);
            _active = true;
        };
    }

    /// <summary>Popups on screen (the <c>ui.popups</c> probe).</summary>
    public int Shown => _feed.ShownCount;

    public override void _Process(double delta)
    {
        if (!_active) return;
        _feed.Tick(delta);
        UpdateAnchor();
        var popups = _feed.Popups;
        float follow = Mathf.Min(1f, (float)delta * Follow);
        bool any = false;
        for (int i = 0; i < popups.Count; i++)
        {
            var p = popups[i];
            if (!p.Shown)
            {
                _wasShown[i] = false;
                continue;
            }
            any = true;
            float target = _feed.Row(i) * PickupFeed.RowPitch + PickupFeed.Rise(p.Age);
            _offsets[i] = _wasShown[i] ? Mathf.Lerp(_offsets[i], target, follow) : target; // a new popup starts in place
            _wasShown[i] = true;
            if (_versions[i] != p.Version || _texts[i] == null)
            {
                _versions[i] = p.Version;
                _texts[i] = p.Text;
                _widths[i] = UiFonts.Bold.GetStringSize(_texts[i], HorizontalAlignment.Left, -1, FontSize).X;
            }
        }
        _active = any;
        QueueRedraw(); // the last frame clears what was drawn
    }

    public override void _Draw()
    {
        if (!_active) return;
        var font = UiFonts.Bold;
        var popups = _feed.Popups;
        for (int i = 0; i < popups.Count; i++)
        {
            var p = popups[i];
            if (!p.Shown || _texts[i] == null) continue;
            float alpha = PickupFeed.Alpha(p.Age), scale = PickupFeed.Scale(p.Age);
            var color = p.IsCoins ? UiTheme.TooltipColor(ItemTooltip.PriceKind(p.Amount), 0) : UiTheme.Rarity(p.Item!.Rarity);
            color.A = alpha;
            // Centred on the anchor and scaled about its own middle for the pop.
            DrawSetTransform(new Vector2(_anchor.X, _anchor.Y - _offsets[i]), 0, new Vector2(scale, scale));
            var baseline = new Vector2(-_widths[i] / 2, 0);
            DrawStringOutline(font, baseline, _texts[i], HorizontalAlignment.Left, -1, FontSize, UiFonts.Outline, new Color(UiFonts.OutlineColor, alpha));
            DrawString(font, baseline, _texts[i], HorizontalAlignment.Left, -1, FontSize, color);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Where the popups stand: over the head in third person, else a fixed point above the crosshair; on screen.</summary>
    private void UpdateAnchor()
    {
        var size = Size;
        var at = new Vector2(size.X / 2, size.Y * FirstPersonHeight);
        var camera = GetViewport().GetCamera3D(); // the view on screen (the free camera too)
        if (!_ui.Player.Rig.FirstPerson && camera != null)
        {
            var head = _ui.Player.GlobalPosition + Vector3.Up * (PlayerEntity.HalfExtents.Y * 2 + HeadClearance);
            if (!camera.IsPositionBehind(head)) at = camera.UnprojectPosition(head);
        }
        // Room above for a full stack; on a tiny viewport the lower bound wins.
        float top = EdgeMargin + PickupFeed.Capacity * PickupFeed.RowPitch;
        _anchor = new Vector2(Mathf.Max(EdgeMargin, Mathf.Min(at.X, size.X - EdgeMargin)), Mathf.Max(top, Mathf.Min(at.Y, size.Y - EdgeMargin)));
    }
}
