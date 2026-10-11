using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Game.Items;
using Strata.Game.Settings;

namespace Strata.Game.UI;

/// <summary>
/// A boss's arrival as an event (X-02), on the <see cref="BossIntroModel"/> timeline for bosses with an intro block
/// (<see cref="Strata.Core.Content.Defs.BossIntro"/>): its head icon, name and subtitle on a title card in the upper middle of the screen,
/// thin cinema bars top and bottom, and the third-person camera pulled back for at most 1.5 s (CameraRig.Framing; the
/// view is never turned). When it falls: its name over "Defeated" and a 0.4 s slow-motion dip of the world's time scale
/// (the victory sting and the defeat punch are BossEffects' and SoundEvents'). Attacking or getting hurt skips the card.
/// Drawn under the HUD (layer 9), so every HUD widget stays on top and nothing here takes a click. While the world is
/// paused or photo mode is on the card waits (photo mode hides this layer with the HUD) and the time scale is restored.
/// The Boss intros setting turns it all off. Glyphs of the boss titles at the card's size are rasterised a couple per
/// frame from the start, so a boss's arrival never waits on the font.
/// </summary>
public sealed partial class BossIntro : CanvasLayer
{
    /// <summary>The cinema bars' height when fully in, as a share of the screen height.</summary>
    private const float BarShare = 0.065f;

    /// <summary>The card's top edge, as a share of the screen height.</summary>
    private const float CardTop = 0.17f;

    private const int TitleSize = 46, SubtitleSize = UiFonts.Banner;
    private const float IconSize = 64f, MaxStep = 0.1f;
    private const int WarmPerFrame = 2;
    private const string DefaultColor = "#af4bff";

    private static readonly Color BarColor = new(0f, 0f, 0f, 0.85f);
    private static BossIntro? _instance;

    private readonly BossIntroModel _model = new();
    private readonly Dictionary<string, EnemyDef> _introByFlag = new();
    private readonly Queue<char> _warm = new();
    private readonly Vector2[] _diamond = new Vector2[4];
    private UiContext _ui = null!;
    private PhotoMode.PhotoModeController? _photo;
    private Control _cardHost = null!;
    private ColorRect _top = null!, _bottom = null!;
    private Texture2D? _icon;
    private Color _accent, _subtitleColor;
    private float _titleWidth, _subtitleWidth;
    private ulong _lastUsec;
    private bool _scaled, _useHeld;

    /// <summary>A boss's title card is up (the HUD then leaves out its optional "has awoken!" banner).</summary>
    public static bool CardShowing => _instance?._model.Active == true;

    /// <summary>The card's timeline (the <c>boss.intro_*</c> probes).</summary>
    public BossIntroModel Model => _model;

    public void Initialize(UiContext ui, PhotoMode.PhotoModeController? photo)
    {
        _ui = ui;
        _photo = photo;
        _instance = this;
        Name = "BossIntro";
        Layer = 9; // under the HUD (10)
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        _top = Bar(root, top: true);
        _bottom = Bar(root, top: false);
        _cardHost = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _cardHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cardHost.Draw += DrawCard;
        root.AddChild(_cardHost);

        var seen = new HashSet<char>();
        foreach (var e in ui.Game.Session.Content.Enemies.All)
        {
            if (e.Boss is not { Minor: false, Intro: not null } boss) continue;
            if (boss.Flag != null) _introByFlag.TryAdd(boss.Flag, e);
            foreach (char c in boss.Title)
                if (c > ' ' && seen.Add(c)) _warm.Enqueue(c);
        }
        var npcs = ui.Game.Npcs;
        npcs.BossSpawned += OnSpawned;
        npcs.BossDefeated += OnDefeated;
        npcs.PlayerHurt += (p, _, _) =>
        {
            if (p == _ui.Entity) _model.Skip();
        };
    }

    private static ColorRect Bar(Control root, bool top)
    {
        var bar = new ColorRect { Color = BarColor, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        bar.SetAnchorsPreset(top ? Control.LayoutPreset.TopWide : Control.LayoutPreset.BottomWide);
        bar.GrowVertical = top ? Control.GrowDirection.End : Control.GrowDirection.Begin;
        root.AddChild(bar);
        return bar;
    }

    public override void _ExitTree()
    {
        RestoreTimeScale();
        if (_ui != null && IsInstanceValid(_ui.Player)) _ui.Player.Rig.Framing = 0f;
        if (_instance == this) _instance = null;
    }

    /// <summary>The boss whose intro block a group member shows: its own, else its group's (the Twins' second eye).</summary>
    private EnemyDef? IntroOwner(EnemyDef def)
    {
        if (def.Boss?.Intro != null) return def;
        return def.Boss?.Flag is { } flag && _introByFlag.TryGetValue(flag, out var lead) ? lead : null;
    }

    private void OnSpawned(Npc npc)
    {
        if (!UserSettings.Current.BossIntros || IntroOwner(npc.Def) is not { Boss.Intro: { } intro } lead) return;
        _model.Awaken(lead.Id, lead.Boss!.Title, intro.Subtitle, intro.Color ?? DefaultColor, intro.Framing, intro.Letterbox);
        Begin(lead, intro);
    }

    private void OnDefeated(Npc npc)
    {
        if (!UserSettings.Current.BossIntros || IntroOwner(npc.Def) is not { Boss.Intro: { } intro } lead) return;
        _model.Defeat(lead.Id, lead.Boss!.Title, intro.Color ?? DefaultColor, intro.Letterbox, slowMotion: true);
        Begin(lead, intro);
    }

    private void Begin(EnemyDef lead, Strata.Core.Content.Defs.BossIntro intro)
    {
        _icon = PortraitIcons.Boss(lead);
        _accent = Color.FromHtml(intro.Color ?? DefaultColor);
        _subtitleColor = _model.Kind == BossCardKind.Defeated ? UiTheme.Gold : _accent; // "Defeated" in gold
        var font = UiFonts.Bold;
        _titleWidth = font.GetStringSize(_model.Title, HorizontalAlignment.Left, -1, TitleSize).X;
        _subtitleWidth = font.GetStringSize(_model.Subtitle, HorizontalAlignment.Left, -1, SubtitleSize).X;
        _useHeld = true; // the press that may have summoned it does not skip it
        Apply();
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        float real = _lastUsec == 0 ? 0f : Mathf.Min((now - _lastUsec) / 1e6f, MaxStep);
        _lastUsec = now;
        WarmGlyphs();
        if (!_ui.Player.Runtime.Simulating || _photo?.Active == true)
        {
            RestoreTimeScale(); // paused or in photo mode: the card waits, at normal speed
            return;
        }
        if (!_model.Active) return;
        bool use = !InputGate.ItemUseBlocked && Input.IsActionPressed("use_primary");
        if (use && !_useHeld) _model.Skip();
        _useHeld = use;
        _model.Advance(real);
        Apply();
    }

    /// <summary>Puts the model's state on screen, on the camera and on the world's time scale.</summary>
    private void Apply()
    {
        _ui.Player.Rig.Framing = _model.Framing;
        float bar = Mathf.Round(_model.Bars * BarShare * _cardHost.Size.Y);
        _top.Visible = _bottom.Visible = bar >= 1f;
        _top.OffsetBottom = bar;
        _bottom.OffsetTop = -bar;
        _cardHost.Visible = _model.Active;
        if (_model.Active) _cardHost.QueueRedraw();
        float scale = _model.TimeScale;
        if (scale >= 1f) RestoreTimeScale();
        else if (_scaled || Engine.TimeScale == 1.0) // never fight a time scale someone else set (the timescale command)
        {
            Engine.TimeScale = scale;
            _scaled = true;
        }
    }

    private void RestoreTimeScale()
    {
        if (!_scaled) return;
        Engine.TimeScale = 1.0;
        _scaled = false;
    }

    private void WarmGlyphs()
    {
        if (_warm.Count == 0) return;
        var font = UiFonts.Bold;
        for (int i = 0; i < WarmPerFrame && _warm.Count > 0; i++)
        {
            char c = _warm.Dequeue();
            font.RenderRange(0, new Vector2I(TitleSize, 0), c, c);
            font.RenderRange(0, new Vector2I(TitleSize, UiFonts.OutlineLarge), c, c);
        }
    }

    /// <summary>The card: head icon, the name settling from a slightly larger size, an ornament rule, the subtitle.</summary>
    private void DrawCard()
    {
        float a = _model.Alpha;
        if (!_model.Active || a <= 0.002f) return;
        var host = _cardHost;
        var font = UiFonts.Bold;
        var outline = new Color(UiFonts.OutlineColor, a);
        float cx = Mathf.Round(host.Size.X / 2), y = Mathf.Round(host.Size.Y * CardTop);
        if (_icon != null) host.DrawTextureRect(_icon, new Rect2(cx - IconSize / 2, y, IconSize, IconSize), false, new Color(1, 1, 1, a));
        y += IconSize + 4;

        float titleHeight = font.GetHeight(TitleSize);
        float s = _model.TitleScale;
        host.DrawSetTransform(new Vector2(cx, y + titleHeight / 2), 0f, new Vector2(s, s));
        var titleBase = new Vector2(-_titleWidth / 2, -titleHeight / 2 + font.GetAscent(TitleSize));
        host.DrawStringOutline(font, titleBase, _model.Title, HorizontalAlignment.Left, -1, TitleSize, UiFonts.OutlineLarge, outline);
        host.DrawString(font, titleBase, _model.Title, HorizontalAlignment.Left, -1, TitleSize, new Color(UiTheme.Text, a));
        host.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        y += titleHeight + 6;

        // A rule each side of a small diamond in the boss's colour, on a dark underlay so it reads on a bright sky.
        float half = Mathf.Max(_titleWidth, _subtitleWidth) * 0.5f + 20f;
        var accent = new Color(_accent, a);
        var shade = new Color(0f, 0f, 0f, 0.6f * a);
        Rule(host, cx, y, half, shade, 5f);
        Rule(host, cx, y, half, accent, 2f);
        _diamond[0] = new Vector2(cx, y - 6);
        _diamond[1] = new Vector2(cx + 6, y);
        _diamond[2] = new Vector2(cx, y + 6);
        _diamond[3] = new Vector2(cx - 6, y);
        host.DrawColoredPolygon(_diamond, accent);
        y += 10;

        var subBase = new Vector2(cx - _subtitleWidth / 2, y + font.GetAscent(SubtitleSize));
        host.DrawStringOutline(font, subBase, _model.Subtitle, HorizontalAlignment.Left, -1, SubtitleSize, UiFonts.OutlineLarge, outline);
        host.DrawString(font, subBase, _model.Subtitle, HorizontalAlignment.Left, -1, SubtitleSize, new Color(_subtitleColor, a));
    }

    private static void Rule(Control host, float cx, float y, float half, Color color, float width)
    {
        host.DrawLine(new Vector2(cx - half, y), new Vector2(cx - 9, y), color, width);
        host.DrawLine(new Vector2(cx + 9, y), new Vector2(cx + half, y), color, width);
    }
}
