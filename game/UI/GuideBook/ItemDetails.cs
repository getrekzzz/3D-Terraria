using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Game.Items;

namespace Strata.Game.UI.GuideBook;

/// <summary>
/// The Items tab's right page: big icon and rarity-coloured name, the tooltip lines, "Obtained from" (drops with %,
/// shops, chests, …), "Crafted from", "Used in" (8 recipes a page) and the recipes it serves as a station. Rebuilt
/// only when the selection changes; rows link to items and creatures, and Back walks the history.
/// </summary>
public sealed class ItemDetails
{
    private const int UsedPerPage = 8;

    private readonly GuideBookContext _ctx;
    private readonly Action<ItemDef> _open;
    private readonly VBoxContainer _page;
    private readonly Stack<ItemDef> _history = new();
    private int _usedPage;

    public ItemDetails(GuideBookContext ctx, Action<ItemDef> open)
    {
        _ctx = ctx;
        _open = open;
        Root = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 6);
        Root.AddChild(_page);
        _page.AddChild(GuidePage.Text("Pick an item on the left to see where it comes from and what it makes.", UiTheme.Muted));
    }

    public ScrollContainer Root { get; }
    public ItemDef? Item { get; private set; }
    public int SourceCount { get; private set; }

    public void Show(ItemDef item, bool remember)
    {
        if (remember && Item != null && Item != item) _history.Push(Item);
        if (Item != item) _usedPage = 0;
        Item = item;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
        var item = Item!;
        var index = _ctx.Index!;
        if (_history.Count > 0)
        {
            var back = new Button { Text = "◀ Back", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            back.Pressed += () => Show(_history.Pop(), false);
            _page.AddChild(back);
        }
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(new TextureRect
        {
            Texture = ItemIcons.Get(item), CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        var name = GuidePage.Text(item.DisplayName, UiTheme.Rarity(item.Rarity), 26);
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(name);
        _page.AddChild(head);
        var lines = _ctx.Ui.Tooltip(new ItemStack(item, 1));
        for (int i = 1; i < lines.Count; i++) _page.AddChild(GuidePage.Text(lines[i].Text, UiTheme.Muted, 15));

        var sources = index.SourcesOf(item);
        var making = index.Recipes.Making(item);
        SourceCount = sources.Count + (making.Count > 0 ? 1 : 0);
        _page.AddChild(GuidePage.Section("Obtained from"));
        if (sources.Count == 0 && making.Count == 0) _page.AddChild(GuidePage.Text("Nobody knows yet.", UiTheme.Muted));
        foreach (var s in sources)
        {
            var link = s.Link;
            Texture2D? icon = link switch
            {
                ItemDef i => ItemIcons.Get(i),
                EnemyDef e => PortraitIcons.Creature(e),
                _ => null,
            };
            string text = s.Detail.Length > 0 ? $"{s.Title}  —  {s.Detail}" : s.Title;
            Action? click = link switch
            {
                ItemDef i => () => _open(i),
                EnemyDef e => () => _ctx.Book.OpenEntry(e.Id),
                _ => null,
            };
            _page.AddChild(GuidePage.Link(text, icon, click, text));
        }
        if (making.Count > 0)
        {
            _page.AddChild(GuidePage.Section("Crafted from"));
            foreach (var r in making) _page.AddChild(GuidePage.RecipeCard(r, _open));
        }
        var used = index.Recipes.Using(item);
        if (used.Count > 0)
        {
            int pages = (used.Count + UsedPerPage - 1) / UsedPerPage;
            _usedPage = Math.Clamp(_usedPage, 0, pages - 1);
            _page.AddChild(GuidePage.Section(pages > 1 ? $"Used in ({used.Count}) — page {_usedPage + 1}/{pages}" : $"Used in ({used.Count})"));
            var grid = new GridContainer { Columns = 2 };
            for (int i = _usedPage * UsedPerPage; i < Math.Min(used.Count, (_usedPage + 1) * UsedPerPage); i++)
            {
                var card = GuidePage.RecipeCard(used[i], _open);
                card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                grid.AddChild(card);
            }
            _page.AddChild(grid);
            if (pages > 1)
            {
                var nav = new HBoxContainer();
                var prev = new Button { Text = "◀", Disabled = _usedPage == 0, FocusMode = Control.FocusModeEnum.None };
                prev.Pressed += () => { _usedPage--; Rebuild(); };
                var next = new Button { Text = "▶", Disabled = _usedPage >= pages - 1, FocusMode = Control.FocusModeEnum.None };
                next.Pressed += () => { _usedPage++; Rebuild(); };
                nav.AddChild(prev);
                nav.AddChild(next);
                _page.AddChild(nav);
            }
        }
        int station = index.Recipes.StationFor(item);
        if (station > 0) _page.AddChild(GuidePage.Text($"Crafting station for {station} recipes.", UiTheme.Good));
        Root.ScrollVertical = 0;
    }
}
