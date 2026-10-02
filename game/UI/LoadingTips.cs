using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Game.Player;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// The world loading screen (X-10): covers the view while the player waits for the ground around them to load (the
/// first spawn, and any teleport that takes longer than a moment) with "Loading world…" and a rotating tip from the
/// Guide's tips (data/guide/tips.json). Tips follow <see cref="UserSettings.LoadingTips"/>; the cover always shows.
/// </summary>
public partial class LoadingTips : CanvasLayer
{
    private const double TipSeconds = 6.0;
    private const double ShowAfter = 0.3; // a teleport shorter than this does not flash the screen
    private const double FadeSeconds = 0.35;

    private ColorRect _cover = null!;
    private Label _tip = null!;
    private PlayerController _player = null!;
    private GuideTipDef[] _tips = System.Array.Empty<GuideTipDef>();
    private double _frozen;
    private double _tipLeft;
    private int _next;
    private bool _first = true;
    private bool _hiding;
    private Tween? _tween;

    /// <summary>The tip on screen (empty when none; for probes and tests).</summary>
    public string CurrentTip => _tip.Visible ? _tip.Text : "";

    public void Initialize(GameWorld game, PlayerController player)
    {
        Name = "LoadingTips";
        Layer = 48; // above the HUD and popups, below the console
        _player = player;
        var all = game.Session.Content.GuideTips.All;
        _tips = new GuideTipDef[all.Count];
        int n = 0;
        foreach (var t in all) _tips[n++] = t;
        _next = _tips.Length > 0 ? (int)(System.DateTime.UtcNow.Ticks / 10_000_000 % _tips.Length) : 0;
        _cover = new ColorRect { Color = new Color(0.03f, 0.04f, 0.09f, 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _cover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var title = new Label { Text = "Loading world...", Theme = UiTheme.Theme, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.SetAnchorsPreset(Control.LayoutPreset.Center);
        title.GrowHorizontal = Control.GrowDirection.Both;
        title.GrowVertical = Control.GrowDirection.Both;
        _tip = new Label
        {
            Theme = UiTheme.Theme,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _tip.AddThemeFontSizeOverride("font_size", 18);
        _tip.AddThemeColorOverride("font_color", UiTheme.Muted);
        _tip.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _tip.OffsetLeft = -420;
        _tip.OffsetRight = 420;
        _tip.OffsetTop = -130;
        _tip.OffsetBottom = -60;
        _cover.AddChild(title);
        _cover.AddChild(_tip);
        AddChild(_cover);
        ShowCover(); // the first load shows at once
    }

    public override void _Process(double delta)
    {
        if (_player.Frozen)
        {
            _frozen += delta;
            if (!_cover.Visible && _frozen >= ShowAfter) ShowCover();
            if (_cover.Visible && (_tipLeft -= delta) <= 0) NextTip();
            return;
        }
        if (_cover.Visible && !_hiding) Hide();
        _frozen = 0;
    }

    private void ShowCover()
    {
        _tween?.Kill();
        _hiding = false;
        _cover.Visible = true;
        _cover.Modulate = Colors.White;
        if (!_first) _cover.Modulate = new Color(1, 1, 1, 0);
        if (!_first) (_tween = CreateTween()).TweenProperty(_cover, "modulate:a", 1f, FadeSeconds * 0.5);
        _first = false;
        NextTip();
    }

    private void NextTip()
    {
        _tipLeft = TipSeconds;
        bool on = UserSettings.Current.LoadingTips && _tips.Length > 0;
        _tip.Visible = on;
        if (!on) return;
        _tip.Text = "Tip: " + HintToast.Format(_tips[_next].Text);
        _next = (_next + 1) % _tips.Length;
    }

    private void Hide()
    {
        _hiding = true;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_cover, "modulate:a", 0f, FadeSeconds);
        _tween.TweenCallback(Callable.From(() => _cover.Visible = false));
    }
}
