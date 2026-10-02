using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Guide;
using Strata.Core.Session;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The Guide book (P5-30): a themed two-page book with bookmark tabs (Items, Bestiary, Progress, NPCs, Tips, Achievements). Toggled with the
/// guide_book key (Esc closes); nothing is built until it first opens, and each tab builds on its first selection.
/// The Items index is built on a worker thread at world launch; the Items tab says "Loading…" until it is ready.
/// A hint's Guide link (X-10) opens the book at that entry when the key is pressed while the hint is up.
/// </summary>
public partial class GuideBookScreen : CanvasLayer
{
    public const string Gate = "guide";
    public const string NodeName = "GuideBook";

    private readonly List<IGuideTab> _tabs = new();
    private readonly Dictionary<IGuideTab, Control> _roots = new();
    private readonly List<Button> _buttons = new();
    private System.Threading.Tasks.Task<GuideIndex>? _indexTask;
    private GuideBookContext _ctx = null!;
    private Control? _root;
    private Control _pages = null!;
    private IGuideTab? _current;
    private string? _hintEntry;
    private ulong _hintUntilMs;

    public bool IsOpen => _root is { Visible: true };
    public bool IndexReady => _ctx.Index != null;
    public IGuideTab? Current => _current;
    public ItemsTab Items { get; } = new();
    public BestiaryTab Bestiary { get; } = new();
    public ProgressTab Progress { get; } = new();
    public NpcsTab Npcs { get; } = new();
    public TipsTab Tips { get; } = new();
    public AchievementsTab Achievements { get; } = new();

    /// <summary>Index of the shown tab (probe ui.book_tab): items 0, bestiary 1, progress 2, npcs 3, tips 4, achievements 5.</summary>
    public int TabIndex => IsOpen && _current != null ? _tabs.IndexOf(_current) : -1;

    public void Initialize(UiContext ui)
    {
        Name = NodeName;
        Layer = 24;
        _ctx = new GuideBookContext { Ui = ui, Book = this };
        _tabs.Add(Items);
        _tabs.Add(Bestiary);
        _tabs.Add(Progress);
        _tabs.Add(Npcs);
        _tabs.Add(Tips);
        _tabs.Add(Achievements);
        _indexTask = GuideIndex.BuildAsync(ui.Game.Session.Content);
        ui.Game.Hints.Shown += OnHint;
    }

    public override void _ExitTree()
    {
        if (_ctx != null) _ctx.Ui.Game.Hints.Shown -= OnHint;
    }

    private void OnHint(PlayerEntity p, HintDef def)
    {
        if (p != _ctx.Ui.Entity || def.Guide == null) return;
        _hintEntry = def.Guide;
        _hintUntilMs = Time.GetTicksMsec() + (ulong)((def.Duration + 5f) * 1000f);
    }

    public override void _Process(double delta)
    {
        if (_ctx.Index == null && _indexTask is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully) _ctx.Index = task.Result;
            else GD.PushError($"guide book index failed: {task.Exception?.GetBaseException().Message}");
            _indexTask = null;
            if (IsOpen) _current?.Shown();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputGate.TextEntry) return;
        bool toggle = e.IsActionPressed("guide_book");
        if (!toggle && !(IsOpen && e.IsActionPressed("pause"))) return;
        GetViewport().SetInputAsHandled();
        if (IsOpen) { Close(); return; }
        if (toggle && _hintEntry != null && Time.GetTicksMsec() < _hintUntilMs && OpenEntry(_hintEntry)) { _hintEntry = null; return; }
        Open();
    }

    /// <summary>Opens the book, optionally at a tab (by key); false when the tab is unknown.</summary>
    public bool Open(string? tab = null)
    {
        IGuideTab? target = tab == null ? _current ?? _tabs[0] : _tabs.Find(t => t.Key == tab);
        if (target == null) return false;
        EnsureBuilt();
        if (!IsOpen)
        {
            _root!.Visible = true;
            InputGate.SetOpen(Gate, true);
            Audio.Sfx.Play("ui_open");
        }
        Select(target);
        return true;
    }

    /// <summary>Opens the book at an entry: an item, a creature or resident, or a guide step or tip.</summary>
    public bool OpenEntry(string id)
    {
        var content = _ctx.Ui.Game.Session.Content;
        IGuideTab? tab = content.GuideSteps.Contains(id) ? Progress
            : content.GuideTips.Contains(id) ? Tips
            : content.Achievements.Contains(id) ? Achievements
            : content.TownNpcs.Contains(id) ? Npcs
            : content.Enemies.Contains(id) ? Bestiary
            : content.Items.Contains(id) ? Items : null;
        if (tab == null || !Open(tab.Key)) return false;
        return tab.Show(id);
    }

    public void Close()
    {
        if (!IsOpen) return;
        _root!.Visible = false;
        InputGate.SetOpen(Gate, false);
        InputGate.TextEntry = false;
        Audio.Sfx.Play("ui_close");
    }

    private void Select(IGuideTab tab)
    {
        if (!_roots.TryGetValue(tab, out var root))
        {
            root = tab.Build(_ctx);
            root.SizeFlagsHorizontal = root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _pages.AddChild(root);
            _roots[tab] = root;
        }
        foreach (var (t, r) in _roots) r.Visible = t == tab;
        for (int i = 0; i < _tabs.Count; i++) _buttons[i].ButtonPressed = _tabs[i] == tab;
        if (_current != tab && _current != null) Audio.Sfx.Play("ui_open", -6f);
        _current = tab;
        tab.Shown();
    }

    private void EnsureBuilt()
    {
        if (_root != null) return;
        _root = new Control { Theme = UiTheme.Theme, Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);
        var book = new PanelContainer();
        book.AddThemeStyleboxOverride("panel", UiTheme.Panel(0.97f));
        book.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        book.OffsetLeft = 60; book.OffsetTop = 40; book.OffsetRight = -60; book.OffsetBottom = -40;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        var title = new Label { Text = "Guide Book", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", UiTheme.Gold);
        header.AddChild(title);
        foreach (var tab in _tabs)
        {
            var b = new Button { Text = tab.Title, ToggleMode = true, CustomMinimumSize = new Vector2(104, 34), FocusMode = Control.FocusModeEnum.None };
            var t = tab;
            b.Pressed += () => Select(t);
            _buttons.Add(b);
            header.AddChild(b);
        }
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(34, 34), FocusMode = Control.FocusModeEnum.None };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);
        _pages = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(_pages);
        book.AddChild(column);
        _root.AddChild(book);
        AddChild(_root);
    }
}
