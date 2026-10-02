using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's mana stars on the Mana region: "Mana" over a column of stars at the top-right corner, 20 mana each up to
/// 10 stars and shared out over the 10 past 200 (<see cref="VitalsDisplay.Stars"/>); the partial star shrinks and fades
/// with what it holds. Redrawn only when mana or its maximum changes.
/// </summary>
public sealed partial class ManaStars : Control
{
    private const float CaptionOverhang = 8; // "Mana" is wider than the column: centred over it, into the margin

    private readonly Texture2D _star = UiSprites.Get("star");
    private VitalRow _row;
    private int _mana = -1, _max = -1;

    public ManaStars()
    {
        Name = "Mana";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Stars drawn (the <c>hud.stars</c> probe while shown).</summary>
    public int Count => _row.Count;

    public void Set(int mana, int max)
    {
        if (mana == _mana && max == _max) return;
        _mana = mana;
        _max = max;
        _row = VitalsDisplay.Stars(mana, max);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCaption(this, "Mana", Size.X);
        for (int i = 0; i < _row.Count; i++)
        {
            float fill = _row.Fill(i);
            float scale = fill >= 1 ? 1 : 0.6f + 0.4f * fill;
            float alpha = fill >= 1 ? 1 : 0.4f + 0.6f * fill;
            var centre = new Vector2(Size.X / 2, UiLayout.Caption + (i + 0.5f) * UiLayout.HeartPitch);
            LifeHearts.DrawIcon(this, _star, centre, scale, new Color(1, 1, 1, alpha));
        }
    }

    /// <summary>The small outlined caption centred over a column <paramref name="width"/> wide, overhanging both sides.</summary>
    internal static void DrawCaption(CanvasItem canvas, string text, float width)
    {
        var font = UiFonts.Bold;
        int size = UiFonts.Small;
        var baseline = new Vector2(-CaptionOverhang, Mathf.Round((UiLayout.Caption + font.GetAscent(size) - font.GetDescent(size)) / 2));
        float span = width + 2 * CaptionOverhang;
        canvas.DrawStringOutline(font, baseline, text, HorizontalAlignment.Center, span, size, UiFonts.Outline, UiFonts.OutlineColor);
        canvas.DrawString(font, baseline, text, HorizontalAlignment.Center, span, size, UiTheme.Text);
    }
}
