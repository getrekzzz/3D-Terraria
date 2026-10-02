using Godot;
using Strata.Core.Gameplay.Combat;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The "bars" life display (Settings, Interface): life as a bar across the Hearts region and mana as a column filling
/// upwards under its caption in the Mana region, in place of the hearts and stars. Values are pushed only when they
/// change, and only while the style is shown.
/// </summary>
public sealed class VitalsBars
{
    private const float BarHeight = 22;
    private const float CaptionOverhang = 8; // under the screen margin on the right, the hearts' gap on the left

    private readonly Control _lifeRegion;
    private readonly ProgressBar _life;
    private readonly Control _manaRegion;
    private readonly ProgressBar _mana;
    private bool _lifePlaced, _manaPlaced, _shown;
    private int _lifeShown = -1, _lifeMax = -1, _manaShown = -1, _manaMax = -1;

    public VitalsBars(Control root)
    {
        _lifeRegion = Region(root, "LifeBar");
        _life = Bar(UiTheme.Life);
        _lifeRegion.AddChild(_life);
        _manaRegion = Region(root, "ManaBar");
        var caption = new OutlinedText
        {
            Name = "ManaCaption", Text = "Mana", FontSize = UiFonts.Small, ClipContents = false,
            Position = new Vector2(-CaptionOverhang, 0), Size = new Vector2(UiLayout.ManaWidth + 2 * CaptionOverhang, UiLayout.Caption),
        };
        _manaRegion.AddChild(caption);
        _mana = Bar(UiTheme.Mana);
        _mana.FillMode = (int)ProgressBar.FillModeEnum.BottomToTop;
        _mana.Position = new Vector2(0, UiLayout.Caption + 2);
        _manaRegion.AddChild(_mana);
    }

    /// <summary>Whether the bars style is chosen (the hearts and stars hide while it is).</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            Show();
        }
    }

    public void Place(UiLayoutResult layout)
    {
        _lifePlaced = LayoutWidgets.Place(_lifeRegion, layout, UiRegion.Hearts);
        _life.Size = new Vector2(_lifeRegion.Size.X, BarHeight);
        _manaPlaced = LayoutWidgets.Place(_manaRegion, layout, UiRegion.Mana);
        _mana.Size = new Vector2(_manaRegion.Size.X, Mathf.Max(0, _manaRegion.Size.Y - _mana.Position.Y));
        Show();
    }

    public void Update(Vitals v)
    {
        if (!_shown) return;
        if (v.Life != _lifeShown || v.MaxLife != _lifeMax)
        {
            _lifeShown = v.Life;
            _lifeMax = v.MaxLife;
            _life.MaxValue = v.MaxLife;
            _life.Value = v.Life;
        }
        if (v.Mana == _manaShown && v.MaxMana == _manaMax) return;
        _manaShown = v.Mana;
        _manaMax = v.MaxMana;
        _mana.MaxValue = v.MaxMana;
        _mana.Value = v.Mana;
    }

    private void Show()
    {
        _lifeRegion.Visible = _shown && _lifePlaced;
        _manaRegion.Visible = _shown && _manaPlaced;
    }

    private static Control Region(Control root, string name)
    {
        var c = LayoutWidgets.Register(new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        root.AddChild(c);
        return c;
    }

    private static ProgressBar Bar(Color color)
    {
        var bar = new ProgressBar { ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", Box(new Color(0, 0, 0, 0.55f)));
        bar.AddThemeStyleboxOverride("fill", Box(color));
        return bar;
    }

    private static StyleBoxFlat Box(Color color) => new()
    {
        BgColor = color, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
    };
}
