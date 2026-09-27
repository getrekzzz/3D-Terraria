using System.Linq;
using System.Text;
using Godot;
using Strata.Core.Gameplay.Crafting;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Recipes available at the player's position (stations in reach, liquids, conditions). Click crafts once,
/// shift-click crafts as many as possible (up to one stack). Ingredients and stations are in the tooltip.
/// </summary>
public sealed partial class CraftingPanel : PanelContainer
{
    private UiContext _ui = null!;
    private VBoxContainer _list = null!;
    private Label _stations = null!;
    private double _refreshTimer;

    public void Initialize(UiContext ui, Vector2 position)
    {
        _ui = ui;
        Position = position;
        CustomMinimumSize = new Vector2(330, 420);
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = "Crafting" });
        _stations = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) };
        _stations.AddThemeColorOverride("font_color", UiTheme.Muted);
        _stations.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_stations);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(310, 380), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_list);
        box.AddChild(scroll);
        AddChild(box);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _refreshTimer -= delta;
        if (_refreshTimer <= 0) Refresh();
    }

    public void Refresh()
    {
        _refreshTimer = 0.5;
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        _stations.Text = env.Stations.Count == 0 ? "No crafting stations nearby" : "Near: " + string.Join(", ", env.Stations.OrderBy(s => s));
        var entries = env.Browse(_ui.Recipes, _ui.Inventory.Backpack);
        foreach (var child in _list.GetChildren()) child.QueueFree();
        foreach (var entry in entries)
        {
            var e = entry;
            var row = new Button
            {
                Text = $"{e.Recipe.Output.DisplayName}{(e.Recipe.Def.Count > 1 ? $" x{e.Recipe.Def.Count}" : "")}{(e.CanCraft ? $"   ({e.MaxCrafts})" : "")}",
                Icon = ItemIcons.Get(e.Recipe.Output),
                ExpandIcon = false,
                Alignment = HorizontalAlignment.Left,
                Disabled = !e.CanCraft,
                TooltipText = Describe(e.Recipe),
                CustomMinimumSize = new Vector2(300, 40),
            };
            row.AddThemeConstantOverride("icon_max_width", 32);
            row.AddThemeColorOverride("font_color", UiTheme.Rarity(e.Recipe.Output.Rarity));
            row.Pressed += () => Craft(e.Recipe, Input.IsKeyPressed(Key.Shift));
            _list.AddChild(row);
        }
    }

    private void Craft(CompiledRecipe recipe, bool many)
    {
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        int times = many ? System.Math.Max(1, recipe.Output.MaxStack / System.Math.Max(1, recipe.Def.Count)) : 1;
        int crafted = 0;
        for (int i = 0; i < times && _ui.Recipes.Craft(recipe, _ui.Inventory.Backpack, env.Stations, env.Context); i++) crafted++;
        if (crafted > 0) Audio.Sfx.Play("craft");
        if (crafted > 0) _ui.Notify($"Crafted {crafted * recipe.Def.Count} {recipe.Output.DisplayName}", UiTheme.Text);
        else _ui.Notify("Not enough room in the backpack", UiTheme.Life);
        Refresh();
    }

    private static string Describe(CompiledRecipe r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Requires:");
        foreach (var ing in r.Inputs)
            sb.AppendLine($"  {ing.Count}x {(ing.Group != null ? $"any {ing.Group.Replace('_', ' ')}" : ing.Accepts.First().DisplayName)}");
        if (r.Def.Stations.Length > 0) sb.AppendLine("At: " + string.Join(", ", r.Def.Stations));
        return sb.ToString().TrimEnd();
    }
}
