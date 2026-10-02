using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// The reference game's achievement popup: a panel slides down at the top centre with the achievement's icon,
/// "Achievement complete!" in gold and its name, stays four seconds and slides away. Unlocks queue up and share one
/// panel, so nothing is built per popup.
/// </summary>
public partial class AchievementToast : CanvasLayer
{
    public const double ShowSeconds = 4.0;
    private const double SlideSeconds = 0.35;
    private const float Top = 24f;

    private readonly Queue<AchievementDef> _queue = new();
    private PanelContainer _panel = null!;
    private TextureRect _icon = null!;
    private Label _name = null!;
    private PlayerEntity _local = null!;
    private GameWorld _game = null!;
    private double _left; // seconds the current popup still shows (0 = idle)
    private Tween? _tween;

    public void Initialize(GameWorld game, PlayerEntity local)
    {
        Name = "AchievementToast";
        Layer = 30;
        _game = game;
        _local = local;
        _panel = new PanelContainer { Theme = UiTheme.Theme, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(0.95f));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(40, 40),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var title = new Label { Text = "Achievement complete!" };
        title.AddThemeColorOverride("font_color", UiTheme.Gold);
        title.AddThemeFontSizeOverride("font_size", 15);
        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 20);
        text.AddChild(title);
        text.AddChild(_name);
        row.AddChild(_icon);
        row.AddChild(text);
        _panel.AddChild(row);
        AddChild(_panel);
        game.Achievements.Unlocked += OnUnlocked;
    }

    public override void _ExitTree()
    {
        if (_game != null) _game.Achievements.Unlocked -= OnUnlocked;
    }

    private void OnUnlocked(PlayerEntity player, AchievementDef def)
    {
        if (player == _local) _queue.Enqueue(def);
    }

    public override void _Process(double delta)
    {
        if (_left > 0)
        {
            _left -= delta;
            if (_left <= SlideSeconds && _left + delta > SlideSeconds) Slide(false);
            if (_left <= 0) _panel.Visible = false;
            return;
        }
        if (_queue.Count > 0) Show(_queue.Dequeue());
    }

    private void Show(AchievementDef def)
    {
        _name.Text = def.DisplayName;
        _icon.Texture = def.Icon != null && _game.Session.Content.Items.TryGet(def.Icon, out var item) ? ItemIcons.Get(item) : null;
        _icon.Visible = _icon.Texture != null;
        _panel.TooltipText = def.Description ?? "";
        _panel.Visible = true;
        _panel.ResetSize();
        _left = ShowSeconds;
        Slide(true);
    }

    /// <summary>Slides the panel in from above the screen (or back out).</summary>
    private void Slide(bool show)
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        float x = (width - _panel.Size.X) * 0.5f;
        float hidden = -_panel.Size.Y - 8f;
        _tween?.Kill();
        if (show) _panel.Position = new Vector2(x, hidden);
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "position", new Vector2(x, show ? Top : hidden), SlideSeconds)
            .SetTrans(Tween.TransitionType.Back).SetEase(show ? Tween.EaseType.Out : Tween.EaseType.In);
    }
}
