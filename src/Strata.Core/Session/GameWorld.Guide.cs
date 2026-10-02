using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Guide;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    private RecipeBook? _recipes;

    /// <summary>Every recipe, compiled once (shared by the crafting menu, commands and the guide).</summary>
    public RecipeBook Recipes => _recipes ??= new RecipeBook(Session.Content);

    /// <summary>Records what each player sees, kills, catches, meets, obtains and crafts.</summary>
    public JournalSystem Journal { get; }

    /// <summary>Unlocks each player's achievements as their journal and the world change.</summary>
    public AchievementTracker Achievements { get; }

    /// <summary>A player crafted items (output, total count) — for the journal and achievements.</summary>
    public event Action<PlayerEntity, ItemDef, int>? Crafted;

    /// <summary>Crafts a recipe up to <paramref name="times"/> times for the player; returns how many crafts succeeded.</summary>
    public int Craft(PlayerEntity player, CompiledRecipe recipe, CraftingEnvironment env, int times = 1)
    {
        int crafted = 0;
        while (crafted < times && Recipes.Craft(recipe, player.State.Inventory, env.Stations, env.Context)) crafted++;
        if (crafted > 0) Crafted?.Invoke(player, recipe.Output, crafted * recipe.Def.Count);
        return crafted;
    }
}
