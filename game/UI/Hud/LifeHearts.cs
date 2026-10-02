using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's life hearts on the Hearts region: 20 life each in rows of 10 (the region's two rows hold the 20 hearts of
/// 400 life), shared out over the 20 hearts past 400 and golden for each Life Fruit (<see cref="VitalsDisplay.Hearts"/>).
/// The partial heart shrinks and fades with what it holds and the last full one beats gently. Drawn straight from the
/// sprites: redrawn when life or its maximum changes and at 15 Hz for the beat while shown, never rebuilt.
/// </summary>
public sealed partial class LifeHearts : Control
{
    private const int PerRow = 10;
    private const double BeatStep = 1.0 / 15;

    private readonly Texture2D _heart = UiSprites.Get("heart");
    private readonly Texture2D _gold = UiSprites.Get("heart_gold");
    private VitalRow _row;
    private int _life = -1, _max = -1, _permanent = -1;
    private double _time, _nextBeat;

    public LifeHearts()
    {
        Name = "Hearts";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Hearts drawn (the <c>hud.hearts</c> probe while shown).</summary>
    public int Count => _row.Count;

    /// <summary>Golden hearts among them (<c>hud.golden_hearts</c>).</summary>
    public int Golden => _row.Golden;

    /// <summary>
    /// Life now, its maximum with buffs (which sets the hearts and what each holds) and the permanent maximum from Life
    /// Crystals and Life Fruit (which gilds them, so Lifeforce never does).
    /// </summary>
    public void Set(int life, int max, int permanentMax, double delta)
    {
        _time += delta;
        if (life != _life || max != _max || permanentMax != _permanent)
        {
            _life = life;
            _max = max;
            _permanent = permanentMax;
            _row = VitalsDisplay.Hearts(life, max, permanentMax);
            QueueRedraw();
        }
        if (_time < _nextBeat || !Visible) return;
        _nextBeat = _time + BeatStep;
        QueueRedraw();
    }

    public override void _Draw()
    {
        int beating = _row.Full - 1;
        for (int i = 0; i < _row.Count; i++)
        {
            float fill = _row.Fill(i);
            float scale = fill >= 1 ? 1 : 0.6f + 0.4f * fill;
            float alpha = fill >= 1 ? 1 : 0.4f + 0.6f * fill;
            if (i == beating) scale *= 1 + 0.04f * Mathf.Sin(4 * (float)_time);
            var centre = new Vector2((i % PerRow + 0.5f) * UiLayout.HeartPitch, (i / PerRow + 0.5f) * UiLayout.HeartPitch);
            DrawIcon(this, i < _row.Golden ? _gold : _heart, centre, scale, new Color(1, 1, 1, alpha));
        }
    }

    /// <summary>A sprite centred on <paramref name="centre"/> at <paramref name="scale"/> of its own size.</summary>
    internal static void DrawIcon(CanvasItem canvas, Texture2D texture, Vector2 centre, float scale, Color modulate)
    {
        var size = texture.GetSize() * scale;
        canvas.DrawTextureRect(texture, new Rect2(centre - size / 2, size), false, modulate);
    }
}
