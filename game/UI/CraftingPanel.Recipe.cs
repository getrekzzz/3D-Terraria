using Godot;
using Strata.Core.Content.Index;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// The selected recipe at work: a click on its slot crafts it onto the cursor (<see cref="RecipeBook.CraftInto"/>: an
/// empty hand takes it, the same stackable item grows), Shift+click crafts as many as fit into the backpack, and a held
/// right button keeps crafting onto the cursor at <see cref="HoldRepeat"/>'s quickening pace with the craft sound at
/// most every 0.1 s, never running on into another recipe. A refusal says why on the status line: the held item, the
/// stations or conditions out of reach, the materials or the backpack.
/// </summary>
public sealed partial class CraftingPanel
{
    private const ulong CraftSoundGapMs = 100;

    private CompiledRecipe? _holding;
    private ulong _lastCraftSoundMs;

    /// <summary>
    /// Clicks the recipe for <paramref name="itemId"/> on the column as the player does (selecting it first), then
    /// <paramref name="times"/> more times (the <c>ui craft</c> command). Returns the crafts made, or -1 when no recipe
    /// listed makes the item.
    /// </summary>
    public int Craft(string itemId, int times)
    {
        if (_env == null) Poll();
        if (_dirty) Update();
        int index = _shown.FindIndex(e => e.Recipe.Output.Id == itemId);
        if (index < 0) return -1;
        var view = _recipeViews[index];
        if (index != _selected) RecipeClicked(view, index, MouseButton.Left, ClickMods.None);
        int made = 0;
        while (made < times && RecipeClicked(view, index, MouseButton.Left, ClickMods.None)) made++;
        return made;
    }

    /// <summary>
    /// A click on a recipe slot: another recipe is selected; the selected one crafts onto the cursor (left, or right and
    /// held), Shift+left as many as fit into the backpack. Returns whether it crafted.
    /// </summary>
    private bool RecipeClicked(ItemSlotView view, int index, MouseButton button, ClickMods mods)
    {
        var recipe = index < _shown.Count ? _shown[index].Recipe : null;
        if (view.Repeating)
        {
            if (recipe != null && recipe == _holding && CraftToCursor(recipe)) return true;
            view.StopRepeat();
            return false;
        }
        if (recipe == null || index != _selected)
        {
            view.StopRepeat();
            if (recipe == null) return false;
            Select(index);
            Audio.Sfx.Play("ui_click");
            return false;
        }
        _holding = recipe;
        if (button == MouseButton.Left && (mods & ClickMods.Shift) != 0) return CraftMany(recipe);
        if (CraftToCursor(recipe)) return true;
        view.StopRepeat();
        return false;
    }

    /// <summary>Crafts once onto the cursor with the craft sound (at most one per 0.1 s); a refusal says why on the status line.</summary>
    private bool CraftToCursor(CompiledRecipe recipe)
    {
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        var inv = _ui.Inventory;
        var cursor = inv.Cursor;
        if (!_ui.Recipes.CraftInto(ref cursor, recipe, inv, env.Stations, env.Context))
        {
            _ui.Notify(Refusal(recipe, env, true), UiTheme.Life);
            return false;
        }
        inv.Cursor = cursor;
        ulong now = Time.GetTicksMsec();
        if (now - _lastCraftSoundMs >= CraftSoundGapMs)
        {
            _lastCraftSoundMs = now;
            Audio.Sfx.Play("craft");
        }
        return true;
    }

    /// <summary>Shift+click: crafts as many as fit into the backpack (up to one stack of the output).</summary>
    private bool CraftMany(CompiledRecipe recipe)
    {
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        int times = System.Math.Max(1, recipe.Output.MaxStack / System.Math.Max(1, recipe.Def.Count));
        int crafted = 0;
        while (crafted < times && _ui.Recipes.Craft(recipe, _ui.Inventory, env.Stations, env.Context)) crafted++;
        if (crafted == 0)
        {
            _ui.Notify(Refusal(recipe, env, false), UiTheme.Life);
            return false;
        }
        Audio.Sfx.Play("craft");
        _ui.Notify($"Crafted {crafted * recipe.Def.Count} {recipe.Output.DisplayName}", UiTheme.Text);
        return true;
    }

    /// <summary>Why a craft was refused: the held stack, a station or condition out of reach, the materials or the backpack.</summary>
    private string Refusal(CompiledRecipe recipe, CraftingEnvironment env, bool toCursor)
    {
        var cursor = _ui.Inventory.Cursor;
        if (toCursor && !RecipeBook.CursorTakes(cursor, recipe)) return cursor.Item == recipe.Output ? "The held stack is full" : "Already holding an item";
        var content = _ui.Game.Session.Content;
        foreach (var station in recipe.Def.Stations)
            if (!env.Stations.Contains(station)) return "Required Objects: " + StationNames.Describe(recipe.Def.Stations, content);
        if (!_ui.Recipes.StationsAndConditionsMet(recipe, env.Stations, env.Context)) return "Requires: " + RecipeText.Conditions(recipe.Def, content);
        return _ui.Recipes.CanCraft(recipe, _ui.Inventory, env.Stations, env.Context) ? "Not enough room in the backpack" : "Not enough materials";
    }
}
