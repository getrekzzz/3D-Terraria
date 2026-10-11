using Godot;

namespace Strata.Game.UI;

/// <summary>
/// The gold frame round what the controller has selected: the slot or button under the screen cursor
/// (<see cref="PadCursor"/>) or the focused menu control (<see cref="PadInput"/>). Fills its layer, takes no clicks, and
/// redraws only when the rectangle changes.
/// </summary>
public sealed partial class PadFocusFrame : Control
{
    private const float Grow = 3f;
    private static StyleBoxFlat? _style;
    private Rect2 _rect;
    private bool _shown;

    public PadFocusFrame()
    {
        Name = "PadFocusFrame";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The frame is on screen (round <see cref="Rect"/>).</summary>
    public bool Shown => _shown;

    public Rect2 Rect => _rect;

    /// <summary>Frames <paramref name="rect"/> (global coordinates of the layer).</summary>
    public void Frame(Rect2 rect)
    {
        if (_shown && rect.IsEqualApprox(_rect)) return;
        _rect = rect;
        _shown = true;
        Visible = true;
        QueueRedraw();
    }

    public void Clear()
    {
        if (!_shown) return;
        _shown = false;
        Visible = false;
    }

    public override void _Draw()
    {
        if (!_shown) return;
        _style ??= new StyleBoxFlat
        {
            DrawCenter = false, BorderColor = UiTheme.Gold, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2,
            BorderWidthBottom = 2, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4, ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 2,
        };
        DrawStyleBox(_style, _rect.Grow(Grow));
    }
}
