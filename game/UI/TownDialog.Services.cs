using System;
using System.Linq;
using Strata.Core.Content.Index;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;

namespace Strata.Game.UI;

/// <summary>
/// The resident's service, read from their definition (<c>service.type</c>): its button label, with the price where
/// there is one ('Heal (4 silver)', 'Taxes (2 silver)'), and what pressing it does. Each answer replaces the line in the
/// box; a trade that went through jingles coins. Reforge hides the box and asks for the reforge slot in the inventory
/// (<see cref="ServiceRequested"/>), where the price of the item placed in it shows. Residents with several services or
/// happiness and housing buttons extend <see cref="ServiceLabel"/> and <see cref="RunService"/> here.
/// </summary>
public sealed partial class TownDialog
{
    /// <summary>A service with a panel of its own was pressed (Reforge): the inventory opens with it in its panel host.</summary>
    public event Action<TownNpc, ServiceMode>? ServiceRequested;

    private readonly System.Collections.Generic.List<(Button Button, TypedSpec Service)> _serviceButtons = new();

    /// <summary>Whether the resident sells anything: their shop, a visitor's rotating pools, or their biome's pylon.</summary>
    private bool HasShop(TownNpc npc) =>
        npc.Def.Kind != Strata.Core.Content.Defs.TownNpcKind.Pet
        && (npc.Def.AllShopEntries.Length > 0 || _ui.Game.Commerce.Stock(npc, _ui.Entity).Count > 0);

    /// <summary>"Shop", then one button per further tab (the Painter's "Decor"), each opening the shop on that tab.</summary>
    private void AddShopButtons(TownNpc npc)
    {
        AddButton("Shop", () => OpenShopTab(ShopTabs.Shop));
        foreach (var tab in _ui.Game.Commerce.Tabs(npc))
            if (tab != ShopTabs.Shop) AddButton(TabLabel(tab), () => OpenShopTab(tab));
    }

    private static string TabLabel(string tab) => tab.Length == 0 ? tab : char.ToUpperInvariant(tab[0]) + tab[1..];

    /// <summary>Opens the grid shop on <paramref name="tab"/> (the shop panel reads <see cref="UiContext.ShopTab"/>).</summary>
    public void OpenShopTab(string tab)
    {
        _ui.ShopTab = tab;
        OpenShop();
    }

    /// <summary>One button per service the resident offers (Terraria gives some several: the Guide's Help and Advice).</summary>
    private void AddServiceButtons(TownNpc npc)
    {
        _serviceButtons.Clear();
        foreach (var service in npc.Def.AllServices)
            if (ServiceLabel(npc, service) is { } label)
            {
                var spec = service;
                Button? made = null;
                made = AddButton(label, () => RunService(spec, made!));
                _serviceButtons.Add((made, spec));
            }
    }

    /// <summary>Prices on service buttons follow the player (the Nurse's heal shrinks as life regenerates).</summary>
    private void RefreshServiceLabels()
    {
        if (_npc == null) return;
        foreach (var (button, service) in _serviceButtons)
            if (ServiceLabel(_npc, service) is { } label && label != button.Text) SetLabel(button, label);
    }

    /// <summary>A service button's label for <paramref name="npc"/> now, or null when it shows no button.</summary>
    private string? ServiceLabel(TownNpc npc, TypedSpec service)
    {
        switch (service.Type)
        {
            case TownServiceSpecs.Heal:
                long heal = _ui.Game.Commerce.HealCost(npc, _ui.Entity);
                return heal > 0 ? $"Heal ({Coins.Format(heal)})" : "Heal";
            case TownServiceSpecs.CraftHelp:
                return "Crafting";
            case TownServiceSpecs.Advice:
                return "Help";
            case TownServiceSpecs.SummonBoss:
                return service.Parse<SummonServiceParams>().Label;
            case TownServiceSpecs.Reforge:
                return "Reforge";
            case TownServiceSpecs.Taxes:
                long due = _ui.Game.Services.TaxesDue(npc);
                return due > 0 ? $"Taxes ({Coins.Format(due)})" : "Taxes";
            case TownServiceSpecs.AnglerQuest:
                return $"Quest ({_ui.Entity.State.AnglerQuests} done)";
            case TownServiceSpecs.WorldStatus:
                return "Status";
            case TownServiceSpecs.Haircut:
                return "Haircut";
            case TownServiceSpecs.DyeTrade:
                return _ui.Game.Services.CanTradeStrangePlant(npc, _ui.Entity) ? service.Parse<DyeTradeParams>().Label : null;
            default:
                return null;
        }
    }

    /// <summary>A service with a screen of its own outside the inventory was pressed (the Stylist's Haircut): the box hides.</summary>
    public event Action<TownNpc, string>? PanelRequested;

    private void OpenPanel(string type)
    {
        var npc = _npc!;
        HideBox();
        PanelRequested?.Invoke(npc, type);
    }

    private void TradeStrangePlant()
    {
        var result = _ui.Game.Services.TradeStrangePlant(_npc!, _ui.Entity, out var dye);
        Say(result == TradeResult.Ok ? $"A Strange Plant! For you, a {dye.Item!.DisplayName}." : "Bring me Strange Plants and I will make you wonders.");
    }

    /// <summary>Presses a service button.</summary>
    private void RunService(TypedSpec service, Button button)
    {
        if (_npc == null) return;
        switch (service.Type)
        {
            case TownServiceSpecs.Heal: Heal(); break;
            case TownServiceSpecs.CraftHelp: CraftHelp(); break;
            case TownServiceSpecs.Advice: Say(_ui.Game.Advisor.Advice(_ui.Entity)); break;
            case TownServiceSpecs.SummonBoss: Summon(); break;
            case TownServiceSpecs.Reforge: OpenService(ServiceMode.Reforge); break;
            case TownServiceSpecs.Taxes: CollectTaxes(); break;
            case TownServiceSpecs.AnglerQuest: AnglerQuest(); break;
            case TownServiceSpecs.WorldStatus: WorldStatus(); break;
            case TownServiceSpecs.Haircut: OpenPanel(service.Type); break;
            case TownServiceSpecs.DyeTrade: TradeStrangePlant(); break;
        }
        if (_npc != null && ServiceLabel(_npc, service) is { } label) SetLabel(button, label);
    }

    /// <summary>Presses the first service of a type (commands and tests); false when the resident has none.</summary>
    public bool PressService(string type)
    {
        foreach (var (button, service) in _serviceButtons)
            if (service.Type == type)
            {
                RunService(service, button);
                return true;
            }
        return false;
    }

    private void WorldStatus()
    {
        var text = _ui.Game.Services.WorldStatusText(_npc!);
        Say(text ?? "Give me a moment to feel the world around us...");
    }

    private void Heal() => Report(_ui.Game.Commerce.Heal(_npc!, _ui.Entity), "You feel much better.");

    private void CraftHelp()
    {
        var held = _ui.Inventory.Held;
        if (held.IsEmpty)
        {
            Say("Hold an item and ask again: I will tell you what it becomes.");
            return;
        }
        var recipes = _ui.Game.Commerce.RecipesUsing(held.Item!);
        Say(recipes.Count == 0
            ? $"I know of nothing made from {held.Item!.DisplayName}."
            : $"{held.Item!.DisplayName} goes into: " + string.Join(", ", recipes.Select(r =>
                $"{_ui.Game.Session.Content.Items.Get(r.Output).DisplayName}{(r.Stations.Length > 0 ? $" (at {StationNames.Describe(r.Stations, _ui.Game.Session.Content)})" : "")}").Distinct()));
    }

    private void Summon()
    {
        var result = _ui.Game.Commerce.Summon(_npc!, _ui.Entity, out var why);
        if (result == TradeResult.Refused) Say(why ?? "Not now.");
        else if (result == TradeResult.NothingToDo) Say("It is already awake.");
        else Report(result, "So be it.");
    }

    /// <summary>
    /// Reforge: the box hides and the inventory shows the resident's reforge slot (the conversation goes on there);
    /// commands and tests press it too. False when the resident offers no such service.
    /// </summary>
    public bool OpenService(ServiceMode mode)
    {
        if (_npc is not { } npc || npc.Def.FindService(ServiceType(mode)) == null) return false;
        HideBox();
        ServiceRequested?.Invoke(npc, mode);
        return true;
    }

    /// <summary>The <c>service.type</c> that offers <paramref name="mode"/>.</summary>
    private static string ServiceType(ServiceMode mode) => mode switch
    {
        ServiceMode.Reforge => TownServiceSpecs.Reforge,
        _ => "",
    };

    private void CollectTaxes()
    {
        var result = _ui.Game.Services.CollectTaxes(_npc!, _ui.Entity, out long amount);
        if (result == TradeResult.Ok) Audio.Sfx.Play("coins");
        Say(result == TradeResult.Ok ? $"Here's your cut: {Coins.Format(amount)}. Don't spend it all at once." : "Nothing's come in yet. Come back later.");
    }

    private void AnglerQuest()
    {
        var services = _ui.Game.Services;
        var wanted = services.AnglerWants(_npc!);
        if (wanted == null)
        {
            Say("You already caught what I wanted today. Come back tomorrow!");
            return;
        }
        var result = services.TurnInFish(_npc!, _ui.Entity, out var reward);
        Say(result switch
        {
            TradeResult.Ok => reward.IsEmpty ? "Finally! Here, take some coins." : $"Finally! Take this: {reward}.",
            _ => $"I want a {wanted.DisplayName}. Go catch me one! Don't come back without it.",
        });
    }

    private void Report(TradeResult result, string success)
    {
        if (result == TradeResult.Ok) Audio.Sfx.Play("coins");
        Say(result switch
        {
            TradeResult.Ok => success,
            TradeResult.TooPoor => "You cannot afford that.",
            TradeResult.NoRoom => "Your pockets are full.",
            TradeResult.NotSold => "I have no use for that.",
            TradeResult.NothingToDo => "You look perfectly healthy to me.",
            _ => "That is not for sale today.",
        });
    }
}
