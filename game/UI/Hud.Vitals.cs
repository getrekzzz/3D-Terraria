using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Presentation;
using Strata.Core.Session;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// Life, mana and breath as Terraria shows them: "Life: 120/140" over the rows of hearts, the column of mana stars at
/// the top-right corner and the breath bubbles above the crosshair while underwater; or, with the "bars" life display,
/// two bars where the hearts and stars go (the bubbles stay).
/// </summary>
public sealed partial class Hud
{
    private OutlinedText _lifeText = null!;
    private LifeHearts _hearts = null!;
    private ManaStars _stars = null!;
    private BreathBubbles _bubbles = null!;
    private VitalsBars _bars = null!;
    private bool _heartsPlaced, _starsPlaced;
    private int _textLife = -1, _textMax = -1;

    /// <summary>Hearts on screen (the <c>hud.hearts</c> probe; 0 with the bars display).</summary>
    public int HeartsShown => _hearts.Visible ? _hearts.Count : 0;

    /// <summary>Golden (Life Fruit) hearts on screen (<c>hud.golden_hearts</c>).</summary>
    public int GoldenHeartsShown => _hearts.Visible ? _hearts.Golden : 0;

    /// <summary>Mana stars on screen (<c>hud.stars</c>).</summary>
    public int StarsShown => _stars.Visible ? _stars.Count : 0;

    /// <summary>Breath bubbles on screen (<c>hud.bubbles</c>; 0 at full breath).</summary>
    public int BubblesShown => _bubbles.Visible ? _bubbles.Shown : 0;

    private void BuildVitals(Control root)
    {
        _lifeText = LayoutWidgets.Register(new OutlinedText { Name = "LifeText" });
        root.AddChild(_lifeText);
        root.AddChild(_hearts = LayoutWidgets.Register(new LifeHearts()));
        root.AddChild(_stars = LayoutWidgets.Register(new ManaStars()));
        root.AddChild(_bubbles = LayoutWidgets.Register(new BreathBubbles()));
        _bars = new VitalsBars(root);
        _bars.Shown = BarsChosen;
    }

    private static bool BarsChosen => UserSettings.Current.LifeDisplay == UserSettings.BarsLifeDisplay;

    private void PlaceVitals(UiLayoutResult layout)
    {
        _lifeText.Visible = LayoutWidgets.Place(_lifeText, layout, UiRegion.LifeText);
        _heartsPlaced = LayoutWidgets.Place(_hearts, layout, UiRegion.Hearts);
        _starsPlaced = LayoutWidgets.Place(_stars, layout, UiRegion.Mana);
        _bubbles.Placed = LayoutWidgets.Place(_bubbles, layout, UiRegion.Breath);
        _bars.Place(layout);
        ShowLifeStyle();
    }

    /// <summary>The hearts and stars, or the bars, as the life display setting says.</summary>
    private void ShowLifeStyle()
    {
        bool bars = BarsChosen;
        _bars.Shown = bars;
        _hearts.Visible = _heartsPlaced && !bars;
        _stars.Visible = _starsPlaced && !bars;
    }

    private void UpdateVitals(double delta)
    {
        if (BarsChosen != _bars.Shown) ShowLifeStyle();
        var state = _ui.Entity.State;
        var v = state.Vitals;
        if (v.Life != _textLife || v.MaxLife != _textMax)
        {
            _textLife = v.Life;
            _textMax = v.MaxLife;
            _lifeText.Text = $"Life: {v.Life}/{v.MaxLife}";
        }
        _hearts.Set(v.Life, v.MaxLife, PlayerState.BaseLife + state.BonusLife, delta);
        _stars.Set(v.Mana, v.MaxMana);
        _bars.Update(v);
        _bubbles.Set(_ui.Entity.Breath, PlayerEntity.MaxBreath, delta);
    }
}
