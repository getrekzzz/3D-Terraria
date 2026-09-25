using System;
using System.Linq;
using Godot;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Talking to a resident: their line, then Shop (buy with Buy buttons, sell the held stack), their service
/// (Heal for a price, crafting Help for the held item) and Close. Closes when the player walks away.
/// </summary>
public sealed partial class TownDialog : CanvasLayer
{
    private const string Gate = "town";
    private UiContext _ui = null!;
    private PanelContainer _root = null!;
    private Label _title = null!;
    private Label _text = null!;
    private Label _coins = null!;
    private VBoxContainer _shop = null!;
    private ScrollContainer _shopScroll = null!;
    private HBoxContainer _buttons = null!;
    private TownNpc? _npc;

    public bool IsOpen => _root.Visible;
    public TownNpc? Resident => _npc;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "TownDialog";
        Layer = 22;
        _root = new PanelContainer { Visible = false, Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(560, 0) };
        _root.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _root.Position = new Vector2(-280, -330);
        AddChild(_root);
        var box = new VBoxContainer();
        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(_title);
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 48) };
        box.AddChild(_text);
        _shopScroll = new ScrollContainer { CustomMinimumSize = new Vector2(540, 190), Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _shop = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _shopScroll.AddChild(_shop);
        box.AddChild(_shopScroll);
        _coins = new Label();
        _coins.AddThemeColorOverride("font_color", UiTheme.Muted);
        box.AddChild(_coins);
        _buttons = new HBoxContainer();
        box.AddChild(_buttons);
        _root.AddChild(box);
        ui.Interaction.TalkRequested += Open;
    }

    public void Open(TownNpc npc)
    {
        _npc = npc;
        _root.Visible = true;
        InputGate.SetOpen(Gate, true);
        _title.Text = npc.Title;
        _text.Text = _ui.Game.Commerce.Greet(npc, _ui.Entity);
        ShowShop(false);
        RebuildButtons();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _root.Visible = false;
        InputGate.SetOpen(Gate, false);
        if (_npc != null) _ui.Game.Town.EndTalk(_npc);
        _npc = null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (IsOpen && e.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _npc == null) return;
        _coins.Text = $"Coins: {Coins.Format(Coins.Total(_ui.Inventory.Backpack))}";
        var far = System.Numerics.Vector3.Distance(_npc.Center, _ui.Entity.Center) > _ui.Game.Town.Config.TalkRange + 1f;
        if (far || !_npc.Alive || !_ui.Entity.Alive || !_ui.Game.Town.Residents.Contains(_npc)) Close();
    }

    private void RebuildButtons()
    {
        foreach (var c in _buttons.GetChildren()) c.QueueFree();
        var npc = _npc!;
        if (npc.Def.Shop.Length > 0) _buttons.AddChild(Button("Shop", () => ShowShop(!_shopScroll.Visible)));
        if (npc.Def.Service is { } service)
        {
            if (service.Type == TownServiceSpecs.Heal)
            {
                long cost = _ui.Game.Commerce.HealCost(npc, _ui.Entity);
                _buttons.AddChild(Button(cost > 0 ? $"Heal ({Coins.Format(cost)})" : "Heal", Heal));
            }
            else if (service.Type == TownServiceSpecs.CraftHelp)
            {
                _buttons.AddChild(Button("Help", CraftHelp));
            }
        }
        _buttons.AddChild(Button("Chat", () => _text.Text = _ui.Game.Commerce.Greet(npc, _ui.Entity)));
        _buttons.AddChild(Button("Close", Close));
    }

    /// <summary>Shows the shop list (commands, tests).</summary>
    public void OpenShop() => ShowShop(true);

    private void ShowShop(bool show)
    {
        _shopScroll.Visible = show;
        if (!show || _npc == null) return;
        foreach (var c in _shop.GetChildren()) c.QueueFree();
        var commerce = _ui.Game.Commerce;
        foreach (var offer in commerce.Stock(_npc, _ui.Entity))
        {
            var row = new HBoxContainer();
            row.AddChild(new TextureRect { Texture = ItemIcons.Get(offer.Item), CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            row.AddChild(new Label { Text = offer.Item.DisplayName, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = string.Join("\n", _ui.Tooltip(new ItemStack(offer.Item, 1)).Select(t => t.Text)) });
            row.AddChild(new Label { Text = Coins.Format(offer.Price) });
            var item = offer.Item;
            row.AddChild(Button("Buy", () => Report(commerce.Buy(_npc, _ui.Entity, item), $"Bought {item.DisplayName}.")));
            _shop.AddChild(row);
        }
        var sell = new HBoxContainer();
        sell.AddChild(Button("Sell held stack", SellHeld));
        _shop.AddChild(sell);
    }

    private void SellHeld()
    {
        var inv = _ui.Inventory;
        var held = inv.Held;
        if (held.IsEmpty) return;
        long value = _ui.Game.Commerce.SaleValue(held);
        Report(_ui.Game.Commerce.Sell(_ui.Entity, inv.SelectedHotbar), $"Sold for {Coins.Format(value)}.");
    }

    private void Heal()
    {
        Report(_ui.Game.Commerce.Heal(_npc!, _ui.Entity), "You feel much better.");
        RebuildButtons();
    }

    private void CraftHelp()
    {
        var held = _ui.Inventory.Held;
        if (held.IsEmpty)
        {
            _text.Text = "Hold an item and ask again: I will tell you what it becomes.";
            return;
        }
        var recipes = _ui.Game.Commerce.RecipesUsing(held.Item!);
        _text.Text = recipes.Count == 0
            ? $"I know of nothing made from {held.Item!.DisplayName}."
            : $"{held.Item!.DisplayName} goes into: " + string.Join(", ", recipes.Select(r =>
                $"{_ui.Game.Session.Content.Items.Get(r.Output).DisplayName}{(r.Stations.Length > 0 ? $" (at {string.Join(" or ", r.Stations)})" : "")}").Distinct());
    }

    private void Report(TradeResult result, string success)
    {
        _text.Text = result switch
        {
            TradeResult.Ok => success,
            TradeResult.TooPoor => "You cannot afford that.",
            TradeResult.NoRoom => "Your pockets are full.",
            TradeResult.NotSold => "I have no use for that.",
            TradeResult.NothingToDo => "You look perfectly healthy to me.",
            _ => "That is not for sale today.",
        };
    }

    private static Button Button(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }
}
