using System.Collections.Generic;
using Godot;
using Strata.Core.Content;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// A tab with a list on the left and the selected entry's page on the right (Progress, NPCs, Tips, Achievements).
/// The list is refilled each time the tab is shown; the page is rebuilt only when the selection changes or the tab
/// is shown again. Optional icons load a few per frame.
/// </summary>
public abstract class ListTab<T> : IGuideTab where T : ContentDef
{
    private const int IconsPerFrame = 8;

    private ItemList _list = null!;
    private VBoxContainer _page = null!;
    private ScrollContainer _scroll = null!;
    private readonly List<T> _entries = new();
    private int _nextIcon;

    protected GuideBookContext Ctx { get; private set; } = null!;

    public abstract string Key { get; }
    public abstract string Title { get; }

    /// <summary>The entry on the right page (null when none).</summary>
    public T? Selected { get; private set; }

    public int Count => _entries.Count;

    protected abstract IEnumerable<T> Entries();
    protected abstract string Label(T entry);
    protected virtual Color? Tint(T entry) => null;
    protected virtual Texture2D? Icon(T entry) => null;
    protected abstract void Render(T entry, VBoxContainer page);
    protected virtual string Intro => "Pick an entry on the left.";

    public Control Build(GuideBookContext ctx)
    {
        Ctx = ctx;
        var root = new Ticker { Tick = Tick };
        var split = new HBoxContainer();
        split.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        _list = new ItemList { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(28, 28) };
        _list.ItemSelected += i => Select(_entries[(int)i]);
        _scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 6);
        _page.AddChild(GuidePage.Text(Intro, UiTheme.Muted));
        _scroll.AddChild(_page);
        split.AddChild(_list);
        split.AddChild(_scroll);
        root.AddChild(split);
        return root;
    }

    public void Shown()
    {
        _entries.Clear();
        _list.Clear();
        foreach (var e in Entries())
        {
            _entries.Add(e);
            int i = _list.AddItem(Label(e));
            if (Tint(e) is { } c) _list.SetItemCustomFgColor(i, c);
        }
        _nextIcon = 0;
        if (Selected != null) Show(Selected.Id);
    }

    public bool Show(string id)
    {
        if (_entries.Count == 0) Shown();
        int at = _entries.FindIndex(e => e.Id == id);
        if (at < 0) return false;
        _list.Select(at);
        _list.EnsureCurrentIsVisible();
        Select(_entries[at]);
        return true;
    }

    private void Select(T entry)
    {
        Selected = entry;
        foreach (var c in _page.GetChildren()) c.QueueFree();
        Render(entry, _page);
        _scroll.ScrollVertical = 0;
    }

    private void Tick()
    {
        if (!_list.IsVisibleInTree()) return;
        for (int n = 0; n < IconsPerFrame && _nextIcon < _entries.Count; n++, _nextIcon++)
            if (Icon(_entries[_nextIcon]) is { } tex) _list.SetItemIcon(_nextIcon, tex);
    }
}
