using System.Collections.Generic;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Game.Player;

namespace Strata.Game.UI;

/// <summary>Everything the gameplay UI reads: the world, the local player and shared helpers.</summary>
public sealed class UiContext
{
    public UiContext(GameWorld game, PlayerController player, InteractionController interaction)
    {
        Game = game;
        Player = player;
        Interaction = interaction;
        Recipes = game.Recipes;
    }

    public GameWorld Game { get; }
    public PlayerController Player { get; }
    public InteractionController Interaction { get; }
    public RecipeBook Recipes { get; }
    public PlayerEntity Entity => Player.Entity;
    public PlayerInventory Inventory => Entity.State.Inventory;

    /// <summary>Short on-screen notification (crafting results, warnings); the HUD shows them as toasts.</summary>
    public event System.Action<string, Godot.Color>? Notified;

    public void Notify(string text, Godot.Color color) => Notified?.Invoke(text, color);

    /// <summary>
    /// Tooltip lines for a stack as the local player sees it: prefix and class bonuses applied, the worn set bonus, and
    /// the town's sell price (or <paramref name="buyPrice"/> in a shop).
    /// </summary>
    public List<(string Text, TooltipKind Kind)> Tooltip(ItemStack stack) => Tooltip(stack, null);

    public List<(string Text, TooltipKind Kind)> Tooltip(ItemStack stack, long? buyPrice) =>
        ItemTooltip.Lines(stack, Game.Uses[stack.Item!], new TooltipContext(Recipes.RecipesUsing(stack.Item!).Count > 0,
            Game.Session.Content, Entity.State, Game.Town.Config.SellRatio, buyPrice));
}
