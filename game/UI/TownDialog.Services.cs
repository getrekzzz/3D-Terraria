using System;
using System.Linq;
using Strata.Core.Content.Index;
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

    /// <summary>A service with a screen of its own outside the inventory was pressed (the Stylist's Haircut): the box hides.</summary>
    public event Action<TownNpc, string>? PanelRequested;

    /// <summary>The service the chat box's button runs: the legacy single service, else the first of the list.</summary>
    private static Strata.Core.Content.TypedSpec? MainService(TownNpc npc) => npc.Def.AllServices.Length > 0 ? npc.Def.AllServices[0] : null;

    private void OpenPanel()
    {
        var npc = _npc!;
        var type = MainService(npc)!.Type;
        HideBox();
        PanelRequested?.Invoke(npc, type);
    }

    /// <summary>The service button's label for <paramref name="npc"/> now, or null when they offer none.</summary>
    private string? ServiceLabel(TownNpc npc)
    {
        if (MainService(npc) is not { } service) return null;
        switch (service.Type)
        {
            case TownServiceSpecs.Heal:
                long heal = _ui.Game.Commerce.HealCost(npc, _ui.Entity);
                return heal > 0 ? $"Heal ({Coins.Format(heal)})" : "Heal";
            case TownServiceSpecs.CraftHelp:
                return "Help";
            case TownServiceSpecs.SummonBoss:
                return service.Parse<SummonServiceParams>().Label;
            case TownServiceSpecs.Reforge:
                return "Reforge";
            case TownServiceSpecs.Taxes:
                long due = _ui.Game.Services.TaxesDue(npc);
                return due > 0 ? $"Taxes ({Coins.Format(due)})" : "Taxes";
            case TownServiceSpecs.AnglerQuest:
                return "Quest";
            case TownServiceSpecs.Haircut:
                return "Haircut";
            default:
                return null;
        }
    }

    /// <summary>Presses the service button.</summary>
    private void RunService()
    {
        if (_npc is null || MainService(_npc) is not { } service) return;
        switch (service.Type)
        {
            case TownServiceSpecs.Heal: Heal(); break;
            case TownServiceSpecs.CraftHelp: CraftHelp(); break;
            case TownServiceSpecs.SummonBoss: Summon(); break;
            case TownServiceSpecs.Reforge: OpenService(ServiceMode.Reforge); break;
            case TownServiceSpecs.Taxes: CollectTaxes(); break;
            case TownServiceSpecs.AnglerQuest: AnglerQuest(); break;
            case TownServiceSpecs.Haircut: OpenPanel(); break;
        }
        if (_npc != null && _serviceButton != null && ServiceLabel(_npc) is { } label) SetLabel(_serviceButton, label);
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
        if (_npc is not { } npc || npc.Def.Service?.Type != ServiceType(mode)) return false;
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
