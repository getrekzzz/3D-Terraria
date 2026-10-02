using Godot;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Life, mana and breath as bars in their layout regions: "Life: 80/100" over the life bar where the hearts go, the
/// mana column at the top-right corner filling upwards under its caption, and the breath bar above the crosshair while
/// underwater. The hearts, stars and bubbles of the reference game take these regions over later.
/// </summary>
public sealed class VitalsBars
{
    private const float BarHeight = 22;
    private const float CaptionOverhang = 8; // under the screen margin on the right, the hearts' gap on the left

    private readonly UiContext _ui;
    private readonly OutlinedText _lifeText;
    private readonly Control _hearts;
    private readonly ProgressBar _life;
    private readonly Control _manaColumn;
    private readonly ProgressBar _mana;
    private readonly ProgressBar _breath;
    private readonly OutlinedText _breathText;
    private bool _breathPlaced;
    private int _shownLife = -1, _shownMax = -1, _shownBreath = -1;

    public VitalsBars(UiContext ui, Control root)
    {
        _ui = ui;
        _lifeText = LayoutWidgets.Register(new OutlinedText { Name = "LifeText" });
        root.AddChild(_lifeText);
        _hearts = Region(root, "Hearts");
        _life = Bar(UiTheme.Life);
        _hearts.AddChild(_life);
        _manaColumn = Region(root, "Mana");
        // "Mana" is a little wider than the column, as in the reference game: centred over it, into the margin.
        var caption = new OutlinedText
        {
            Name = "ManaCaption", Text = "Mana", FontSize = UiFonts.Small, ClipContents = false,
            Position = new Vector2(-CaptionOverhang, 0), Size = new Vector2(UiLayout.ManaWidth + 2 * CaptionOverhang, UiLayout.Caption),
        };
        _manaColumn.AddChild(caption);
        _mana = Bar(UiTheme.Mana);
        _mana.FillMode = (int)ProgressBar.FillModeEnum.BottomToTop;
        _mana.Position = new Vector2(0, UiLayout.Caption + 2);
        _manaColumn.AddChild(_mana);
        _breath = LayoutWidgets.Register(Bar(new Color(0.55f, 0.85f, 1f)));
        _breath.Name = "Breath";
        _breath.MaxValue = PlayerEntity.MaxBreath;
        _breath.Visible = false;
        _breathText = new OutlinedText { Name = "BreathText", FontSize = UiFonts.Small };
        _breathText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _breath.AddChild(_breathText);
        root.AddChild(_breath);
    }

    public void Place(UiLayoutResult layout)
    {
        _lifeText.Visible = LayoutWidgets.Place(_lifeText, layout, UiRegion.LifeText);
        _hearts.Visible = LayoutWidgets.Place(_hearts, layout, UiRegion.Hearts);
        _life.Size = new Vector2(_hearts.Size.X, BarHeight);
        _manaColumn.Visible = LayoutWidgets.Place(_manaColumn, layout, UiRegion.Mana);
        _mana.Size = new Vector2(_manaColumn.Size.X, Mathf.Max(0, _manaColumn.Size.Y - _mana.Position.Y));
        _breathPlaced = LayoutWidgets.Place(_breath, layout, UiRegion.Breath);
        _breath.Visible = _breathPlaced && _shownBreath is >= 0 and < PlayerEntity.MaxBreath;
    }

    public void Update()
    {
        var v = _ui.Entity.State.Vitals;
        if (v.Life != _shownLife || v.MaxLife != _shownMax)
        {
            _shownLife = v.Life;
            _shownMax = v.MaxLife;
            _life.MaxValue = v.MaxLife;
            _life.Value = v.Life;
            _lifeText.Text = $"Life: {v.Life}/{v.MaxLife}";
        }
        _mana.MaxValue = v.MaxMana;
        _mana.Value = v.Mana;
        int breath = _ui.Entity.Breath;
        if (breath == _shownBreath) return;
        _shownBreath = breath;
        _breath.Visible = _breathPlaced && breath < PlayerEntity.MaxBreath;
        _breath.Value = breath;
        _breathText.Text = breath > 0 ? $"Breath {breath * 100 / PlayerEntity.MaxBreath}%" : "Drowning!";
    }

    private static Control Region(Control root, string name)
    {
        var c = LayoutWidgets.Register(new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore });
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
