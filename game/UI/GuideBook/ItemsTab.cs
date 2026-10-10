using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The Items tab (P5-30b): search box and category filter over every item (names at once, icons 16 a frame), and the
/// selected item's page (<see cref="ItemDetails"/>) on the right.
/// </summary>
public sealed class ItemsTab : IGuideTab
{
    private const int IconsPerFrame = 16;

    private GuideBookContext _ctx = null!;
    private LineEdit _search = null!;
    private OptionButton _category = null!;
    private ItemList _list = null!;
    private Label _loading = null!;
    private ItemDetails? _details;
    private List<ItemDef> _shown = new();
    private int _nextIcon;
    private bool _filled;

    public string Key => "items";
    public string Title => "Items";

    /// <summary>The item on the right page (null when none).</summary>
    public ItemDef? Selected => _details?.Item;

    /// <summary>"Obtained from" rows of the shown item (probe ui.book_sources).</summary>
    public int SourceCount => _details?.SourceCount ?? 0;

    public int ListedCount => _shown.Count;

    public Control Build(GuideBookContext ctx)
    {
        _ctx = ctx;
        var root = new Ticker { Tick = Tick };
        var split = new HBoxContainer();
        split.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        _search = new LineEdit { PlaceholderText = "Search items…", ClearButtonEnabled = true };
        _search.TextChanged += _ => Refill();
        _search.FocusEntered += () => InputGate.TextEntry = true;
        _search.FocusExited += () => InputGate.TextEntry = false;
        _category = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _category.AddItem("All categories", 0);
        foreach (var c in System.Enum.GetValues<ItemCategory>()) _category.AddItem(c.ToString(), (int)c + 1);
        _category.ItemSelected += _ => Refill();
        _loading = new Label { Text = "Loading…" };
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(24, 24) };
        _list.ItemSelected += i => Select(_shown[(int)i], true);
        left.AddChild(_search);
        left.AddChild(_category);
        left.AddChild(_loading);
        left.AddChild(_list);
        split.AddChild(left);
        _details = new ItemDetails(ctx, item => Show(item.Id));
        split.AddChild(_details.Root);
        root.AddChild(split);
        return root;
    }

    public void Shown()
    {
        _loading.Visible = _ctx.Index == null;
        if (!_filled && _ctx.Index != null) Refill();
    }

    public bool Show(string id)
    {
        if (_ctx.Index == null || !_ctx.Ui.Game.Session.Content.Items.TryGet(id, out var item)) return false;
        if (!_filled) Refill();
        int at = _shown.IndexOf(item);
        if (at < 0)
        {
            _search.Text = "";
            _category.Selected = 0;
            Refill();
            at = _shown.IndexOf(item);
        }
        if (at >= 0)
        {
            _list.Select(at);
            _list.EnsureCurrentIsVisible();
        }
        Select(item, true);
        return true;
    }

    private void Select(ItemDef item, bool remember) => _details!.Show(item, remember);

    private void Refill()
    {
        if (_ctx.Index == null) return;
        _filled = true;
        ItemCategory? cat = _category.Selected > 0 ? (ItemCategory)(_category.GetItemId(_category.Selected) - 1) : null;
        _shown = _ctx.Index.Search(_search.Text, cat);
        _list.Clear();
        foreach (var item in _shown)
        {
            int i = _list.AddItem(item.DisplayName);
            _list.SetItemCustomFgColor(i, UiTheme.Rarity(item.Rarity));
        }
        _nextIcon = 0;
    }

    private void Tick()
    {
        if (!_list.IsVisibleInTree()) return;
        for (int n = 0; n < IconsPerFrame && _nextIcon < _shown.Count; n++, _nextIcon++)
            _list.SetItemIcon(_nextIcon, ItemIcons.Get(_shown[_nextIcon]));
    }
}
