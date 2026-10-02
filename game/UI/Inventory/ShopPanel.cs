using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// A resident's shop in the inventory's panel host, as Terraria shows it: their name over a 10×4 grid of read-only shop
/// slots holding the stock and then, tinted, what the player sold lately (<see cref="TownCommerce.Buyback"/>, newest
/// first), paged with arrow links once there are more than 40. The tooltip ends with the 'Buy price' in coin colours.
/// A click buys one onto the cursor (clicking again stacks it), a held right button keeps buying one at
/// <see cref="HoldRepeat"/>'s quickening pace, Shift buys into the backpack, and a click on a sold stack buys it back
/// for what it fetched. A click on the grid holding anything else sells it, dropped on a cell or between them;
/// Shift-clicking a backpack slot sells too (<c>GameWorld.ClickSlot</c> with the shop as its context). Coins jingle on
/// a trade and a refusal goes to the status line. The stock is read again every second (prices follow the resident's
/// mood, wares the time of day). The resident waits while the shop is open; it closes when they are out of talking
/// range, and putting it away ends the conversation. The 40 slot views are made once.
/// </summary>
public sealed partial class ShopPanel : Control
{
    public const int Columns = 10;
    public const int Cells = 40;

    private const double RestockSeconds = 1;
    private const double CoinSoundGap = 0.05;
    private const float PagerHeight = 24;

    private readonly UiContext _ui;
    private readonly ItemSlotView[] _views = new ItemSlotView[Cells];
    private readonly List<ShopOffer> _offers = new();
    private readonly List<ItemStack> _wares = new(); // one of each offer, as its cell shows it
    private readonly OutlinedText _title;
    private readonly OutlinedText _pageText;
    private readonly Button _prev;
    private readonly Button _next;
    private int _page;
    private int _pageShown = -1;
    private int _pagesShown = -1;
    private double _restockWait;
    private ulong _lastCoinsMs;
    private float _gridWidth;

    public ShopPanel(UiContext ui)
    {
        _ui = ui;
        Name = "ShopPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_title = new OutlinedText { Align = HorizontalAlignment.Left });
        for (int i = 0; i < Cells; i++)
        {
            int cell = i;
            ItemSlotView view = null!;
            view = new ItemSlotView(() => Cell(cell), UiLayout.PanelSlot, SlotKind.Shop, s => TooltipOf(s, cell)) { RepeatRight = true };
            view.Clicked += (button, mods) => Click(cell, button == MouseButton.Right, mods, view);
            AddChild(view);
            _views[i] = view;
        }
        AddChild(_prev = ChestPanel.TextButton("<", () => Turn(-1), "Previous page"));
        AddChild(_next = ChestPanel.TextButton(">", () => Turn(1), "Next page"));
        AddChild(_pageText = new OutlinedText { FontSize = UiFonts.Small });
    }

    /// <summary>The shop tab shown ("shop", or e.g. the Painter's "decor").</summary>
    public string Tab { get; private set; } = Strata.Core.Content.Defs.ShopTabs.Shop;

    /// <summary>The resident whose shop is shown, if any.</summary>
    public TownNpc? Npc { get; private set; }

    /// <summary>Cells showing an item on this page, stock and buyback (the <c>ui.shop_slots</c> probe).</summary>
    public int SlotsShown { get; private set; }

    /// <summary>The resident went out of talking range or is gone: the screen closes the panel.</summary>
    public event Action? CloseRequested;

    private IReadOnlyList<ItemStack> Sold => _ui.Game.Commerce.Buyback(_ui.Entity);

    private int Pages => Math.Max(1, (_wares.Count + Sold.Count + Cells - 1) / Cells);

    /// <summary>Shows <paramref name="npc"/>'s wares from the first page (the screen puts any other panel away first).</summary>
    public void Open(TownNpc npc)
    {
        Npc = npc;
        _page = 0;
        Tab = _ui.ShopTab;
        _ui.ShopTab = Strata.Core.Content.Defs.ShopTabs.Shop;
        _title.Text = Tab == Strata.Core.Content.Defs.ShopTabs.Shop ? npc.Title : $"{npc.Title} — {char.ToUpperInvariant(Tab[0])}{Tab[1..]}";
        Restock();
        Refresh();
    }

    /// <summary>The shop is put away: the conversation ends and the resident goes about their day.</summary>
    public void Release()
    {
        var npc = Npc;
        Npc = null;
        _offers.Clear();
        _wares.Clear();
        SlotsShown = 0;
        if (npc != null && npc.TalkingTo == _ui.Entity) _ui.Game.Town.EndTalk(npc);
    }

    /// <summary>The view of cell <paramref name="cell"/> on the current page (the <c>ui hover shop</c> command), or null.</summary>
    public ItemSlotView? View(int cell) => (uint)cell < Cells ? _views[cell] : null;

    public override void _Process(double delta)
    {
        if (Npc is not { } npc || !IsVisibleInTree()) return;
        var town = _ui.Game.Town;
        if (!npc.Alive || !_ui.Entity.Alive || !town.Residents.Contains(npc)
            || System.Numerics.Vector3.Distance(npc.Center, _ui.Entity.Center) > town.Config.TalkRange + 1f)
        {
            CloseRequested?.Invoke();
            return;
        }
        town.HoldTalk(npc, _ui.Entity);
        if ((_restockWait -= delta) <= 0) Restock();
        Refresh();
    }

    public override void _GuiInput(InputEvent e)
    {
        // Dropping the held stack between the cells sells it too.
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || mb.Position.X > _gridWidth
            || mb.Position.Y < UiLayout.Caption || Npc == null || _ui.Inventory.Cursor.IsEmpty) return;
        AcceptEvent();
        Feedback(_ui.Game.Commerce.SellCursor(_ui.Entity));
    }

    /// <summary>The resident's name on the caption row, the cells in rows of ten and the page links on the right.</summary>
    public void Arrange(float slotScale)
    {
        float side = UiLayout.PanelSlot * slotScale, pitch = side + UiLayout.Gap, top = UiLayout.Caption;
        _gridWidth = Columns * pitch - UiLayout.Gap;
        for (int i = 0; i < Cells; i++)
        {
            _views[i].Position = new Vector2(i % Columns * pitch, top + i / Columns * pitch);
            _views[i].CustomMinimumSize = _views[i].Size = new Vector2(side, side);
        }
        _title.Position = Vector2.Zero;
        _title.Size = new Vector2(_gridWidth, top);
        float x = _gridWidth + 2 * UiLayout.Gap, third = (Size.X - x) / 3;
        _prev.Position = new Vector2(x, top);
        _pageText.Position = new Vector2(x + third, top);
        _next.Position = new Vector2(x + 2 * third, top);
        _prev.Size = _pageText.Size = _next.Size = new Vector2(third, PagerHeight);
    }

    /// <summary>Clicks the cell selling <paramref name="itemId"/> <paramref name="times"/> times (the <c>ui buy</c> command); returns the clicks that bought, or -1 when not on sale.</summary>
    public int Buy(string itemId, int times)
    {
        int at = _wares.FindIndex(w => w.Item!.Id == itemId);
        if (at < 0) return -1;
        _page = at / Cells;
        int bought = 0;
        for (int i = 0; i < times; i++)
        {
            var result = Press(at % Cells, false, ClickMods.None);
            Feedback(result);
            if (result != TradeResult.Ok) break;
            bought++;
        }
        return bought;
    }

    /// <summary>
    /// Shift-clicks sold stack <paramref name="index"/> with an empty cursor (the <c>ui buyback</c> command): it goes
    /// back into the backpack. Null when there is no such stack or something is held (the click would sell it).
    /// </summary>
    public TradeResult? BuyBack(int index)
    {
        if (index < 0 || index >= Sold.Count || !_ui.Inventory.Cursor.IsEmpty) return null;
        int at = _wares.Count + index;
        _page = at / Cells;
        var result = Press(at % Cells, false, ClickMods.Shift);
        Feedback(result);
        return result;
    }

    private void Click(int cell, bool right, ClickMods mods, ItemSlotView view)
    {
        var result = Press(cell, right, mods);
        // Only buying one ware at a time repeats; a sale, a buyback or a refusal ends the hold.
        if (result != TradeResult.Ok || cell + _page * Cells >= _wares.Count) view.StopRepeat();
        Feedback(result);
    }

    /// <summary>What a click on <paramref name="cell"/> does; null when nothing happens.</summary>
    private TradeResult? Press(int cell, bool right, ClickMods mods)
    {
        if (Npc is not { } npc) return null;
        var commerce = _ui.Game.Commerce;
        var cursor = _ui.Inventory.Cursor;
        bool shift = (mods & ClickMods.Shift) != 0;
        int at = _page * Cells + cell;
        if (at < _wares.Count)
        {
            var ware = _wares[at];
            if (cursor.IsEmpty || cursor.CanStackWith(ware))
                return commerce.Buy(npc, _ui.Entity, ware.Item!, 1, shift && cursor.IsEmpty ? BuyTarget.Backpack : BuyTarget.Cursor, Tab);
        }
        else if (cursor.IsEmpty)
        {
            int sold = at - _wares.Count;
            return sold < Sold.Count ? commerce.BuyBack(_ui.Entity, sold, shift ? BuyTarget.Backpack : BuyTarget.Cursor) : null;
        }
        return right ? null : commerce.SellCursor(_ui.Entity); // holding something else: it is sold
    }

    /// <summary>Coins on a trade (at most every 50 ms while a held button buys); a refusal on the status line.</summary>
    private void Feedback(TradeResult? result)
    {
        string? refusal = result switch
        {
            TradeResult.TooPoor => "You don't have enough money.",
            TradeResult.NoRoom => "You can't carry any more.",
            TradeResult.NotSold => "That can't be sold.",
            TradeResult.Unavailable => "That is no longer for sale.",
            _ => null,
        };
        if (refusal != null) _ui.Notify(refusal, UiTheme.Muted);
        if (result != TradeResult.Ok) return;
        ulong now = Time.GetTicksMsec();
        if (now - _lastCoinsMs < CoinSoundGap * 1000) return;
        _lastCoinsMs = now;
        Audio.Sfx.Play("coins");
    }

    private void Turn(int step)
    {
        _page = Math.Clamp(_page + step, 0, Pages - 1);
        Refresh();
    }

    /// <summary>Reads the resident's stock again (conditions and prices change with the time and their mood).</summary>
    private void Restock()
    {
        _restockWait = RestockSeconds;
        _offers.Clear();
        _offers.AddRange(_ui.Game.Commerce.Stock(Npc!, _ui.Entity, Tab));
        _wares.Clear();
        foreach (var offer in _offers) _wares.Add(new ItemStack(offer.Item, 1));
    }

    /// <summary>Tints the cells (stock, then buyback), counts the filled ones and shows the page links when paged.</summary>
    private void Refresh()
    {
        int pages = Pages, filled = _wares.Count + Sold.Count;
        _page = Math.Min(_page, pages - 1);
        int shown = 0;
        for (int i = 0; i < Cells; i++)
        {
            int at = _page * Cells + i;
            _views[i].Kind = at < _wares.Count ? SlotKind.Shop : SlotKind.Buyback;
            if (at < filled) shown++;
        }
        SlotsShown = shown;
        bool paged = pages > 1;
        _prev.Visible = _next.Visible = _pageText.Visible = paged;
        if (!paged || (_page == _pageShown && pages == _pagesShown)) return;
        _pageShown = _page;
        _pagesShown = pages;
        _pageText.Text = $"{_page + 1}/{pages}";
        _prev.Disabled = _page == 0;
        _next.Disabled = _page == pages - 1;
    }

    private ItemStack Cell(int cell)
    {
        int at = _page * Cells + cell;
        if (at < _wares.Count) return _wares[at];
        var sold = Sold;
        at -= _wares.Count;
        return at < sold.Count ? sold[at] : ItemStack.Empty;
    }

    /// <summary>
    /// A cell's tooltip: coin prices as 'Buy price'; a ware paid in another item (the Tavernkeep's Defender Medals)
    /// shows that cost instead.
    /// </summary>
    private List<(string Text, TooltipKind Kind)> TooltipOf(ItemStack stack, int cell)
    {
        int at = _page * Cells + cell;
        if (at < _offers.Count && _offers[at].Currency is { } currency)
        {
            var lines = _ui.Tooltip(stack, null);
            lines.Add(($"Buy price: {_offers[at].Price} {currency.DisplayName}", TooltipKind.Info));
            return lines;
        }
        return _ui.Tooltip(stack, PriceOf(cell));
    }

    /// <summary>The 'Buy price' in a cell's tooltip: the offer's price for one, or what a sold stack fetched.</summary>
    private long? PriceOf(int cell)
    {
        int at = _page * Cells + cell;
        if (at < _offers.Count) return _offers[at].Price;
        var sold = Sold;
        at -= _offers.Count;
        return at < sold.Count ? _ui.Game.Commerce.SaleValue(sold[at]) : null;
    }
}
