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
        Recipes = new RecipeBook(game.Session.Content);
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

    public List<(string Text, TooltipKind Kind)> Tooltip(ItemStack stack) =>
        ItemTooltip.Lines(stack, Game.Uses[stack.Item!], Recipes.RecipesUsing(stack.Item!).Count > 0);
}
