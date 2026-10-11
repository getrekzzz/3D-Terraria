using System.Collections.Generic;
using System.Text;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// The onboarding hint toast (X-10): a small themed panel above the hotbar with the hint and, when it links one, the
/// Guide book entry to read. Fades in, stays the hint's duration, fades out; one pooled panel, hints queue. Follows
/// the <see cref="UserSettings.ShowHints"/> setting both ways (the console's <c>hint on|off</c> saves it).
/// </summary>
public partial class HintToast : CanvasLayer
{
    private const double FadeSeconds = 0.4;
    private const float Bottom = 150f;

    private readonly Queue<HintDef> _queue = new();
    private PanelContainer _panel = null!;
    private Label _text = null!;
    private Label _guide = null!;
    private GameWorld _game = null!;
    private PlayerEntity _local = null!;
    private double _left;
    private Tween? _tween;

    /// <summary>
    /// Holds hints back (the Guide book is open over the screen): a hint on screen is put away and new ones wait in the
    /// queue until this clears.
    /// </summary>
    public bool Suppressed { get; set; }

    /// <summary>A hint is on screen (probe ui.hint_visible).</summary>
    public bool Showing => _panel.Visible;

    public void Initialize(GameWorld game, PlayerEntity local)
    {
        Name = "HintToast";
        Layer = 28;
        _game = game;
        _local = local;
        _panel = new PanelContainer { Theme = UiTheme.Theme, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(0.85f));
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
        _text.AddThemeFontSizeOverride("font_size", 18);
        _guide = new Label();
        _guide.AddThemeColorOverride("font_color", UiTheme.Gold);
        _guide.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_text);
        box.AddChild(_guide);
        _panel.AddChild(box);
        AddChild(_panel);
        var settings = UserSettings.Current;
        game.Hints.Enabled = settings.ShowHints;
        game.Hints.Shown += OnShown;
        game.Hints.EnabledChanged += OnEnabledChanged;
        settings.Changed += OnSettings;
    }

    public override void _ExitTree()
    {
        if (_game == null) return;
        _game.Hints.Shown -= OnShown;
        _game.Hints.EnabledChanged -= OnEnabledChanged;
        UserSettings.Current.Changed -= OnSettings;
    }

    private void OnShown(PlayerEntity player, HintDef def)
    {
        if (player == _local) _queue.Enqueue(def);
    }

    private void OnSettings(UserSettings s) => _game.Hints.Enabled = s.ShowHints;

    private static void OnEnabledChanged(bool on)
    {
        var s = UserSettings.Current;
        if (s.ShowHints == on) return;
        s.ShowHints = on;
        s.Save();
        s.Apply();
    }

    public override void _Process(double delta)
    {
        if (Suppressed)
        {
            if (_panel.Visible) { _tween?.Kill(); _panel.Visible = false; _left = 0; }
            return;
        }
        if (_left > 0)
        {
            _left -= delta;
            if (_left <= FadeSeconds && _left + delta > FadeSeconds) Fade(false);
            if (_left <= 0) _panel.Visible = false;
            return;
        }
        if (_queue.Count > 0) Show(_queue.Dequeue());
    }

    private void Show(HintDef def)
    {
        if (!_game.Hints.Enabled) { _queue.Clear(); return; }
        _text.Text = Format(def.Text);
        string? guide = GuideName(def.Guide);
        _guide.Text = guide != null ? $"Guide book: {guide}  [{KeyBindings.Label("guide_book")}]" : "";
        _guide.Visible = guide != null;
        _panel.Visible = true;
        _panel.ResetSize();
        var view = GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2((view.X - _panel.Size.X) * 0.5f, view.Y - Bottom - _panel.Size.Y);
        _left = def.Duration;
        Fade(true);
    }

    private string? GuideName(string? key)
    {
        if (key == null) return null;
        var content = _game.Session.Content;
        if (content.GuideSteps.TryGet(key, out var step)) return step.DisplayName;
        return content.GuideTips.TryGet(key, out var tip) ? tip.DisplayName : null;
    }

    private void Fade(bool show)
    {
        _tween?.Kill();
        if (show) _panel.Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(_panel, "modulate:a", show ? 1f : 0f, FadeSeconds);
    }

    /// <summary>Replaces <c>{key:action}</c> with the action's bound key(s), as in the Guide's tips.</summary>
    public static string Format(string text)
    {
        if (!text.Contains("{key:")) return text;
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("{key:", i, System.StringComparison.Ordinal);
            int close = open < 0 ? -1 : text.IndexOf('}', open);
            if (close < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, open - i);
            string action = text.Substring(open + 5, close - open - 5);
            sb.Append('[').Append(InputMap.HasAction(action) ? KeyBindings.Label(action) : action).Append(']');
            i = close + 1;
        }
        return sb.ToString();
    }
}
