using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Strata.Core.Content.Index;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Presentation;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Recipes available at the player's position (stations in reach, liquids, conditions), in the layout's Crafting
/// region at the bottom-left of the inventory screen: it takes the region's size and has no minimum of its own beyond
/// its title lines, so the list scrolls instead of covering anything. Click crafts once, shift-click crafts as many as
/// possible (up to one stack); ingredients and stations are in the tooltip. Rows are pooled: a refresh relabels them
/// instead of building new buttons.
/// </summary>
public sealed partial class CraftingPanel : PanelContainer
{
    private const float RowHeight = 36;
    private const double RefreshSeconds = 0.5;

    private readonly List<Button> _rows = new();
    private readonly List<CompiledRecipe?> _rowRecipes = new();
    private List<CraftingEnvironment.Entry> _entries = new();
    private UiContext _ui = null!;
    private VBoxContainer _list = null!;
    private OutlinedText _stations = null!;
    private string _stationsShown = "";
    private double _refreshTimer;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        ClipContents = true;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(new OutlinedText { Text = "Crafting", Align = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, UiLayout.TextLine) });
        _stations = new OutlinedText
        {
            FontSize = UiFonts.Small, Align = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, UiFonts.Small + 3),
            MouseFilter = MouseFilterEnum.Pass, SelfModulate = UiTheme.Muted,
        };
        box.AddChild(_stations);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
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
        _refreshTimer = RefreshSeconds;
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        var stations = env.Stations.Count == 0 ? "No crafting stations nearby" : "Near: " + string.Join(", ", env.Stations.OrderBy(s => s));
        if (stations != _stationsShown)
        {
            _stationsShown = stations;
            _stations.Text = stations;
            _stations.TooltipText = stations; // the line may be cut short
        }
        _entries = env.Browse(_ui.Recipes, _ui.Inventory);
        while (_rows.Count < _entries.Count) AddRow();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Visible = i < _entries.Count;
            if (!row.Visible) continue;
            var e = _entries[i];
            row.Text = $"{e.Recipe.Output.DisplayName}{(e.Recipe.Def.Count > 1 ? $" x{e.Recipe.Def.Count}" : "")}{(e.CanCraft ? $"   ({e.MaxCrafts})" : "")}";
            row.Disabled = !e.CanCraft;
            if (_rowRecipes[i] == e.Recipe) continue;
            _rowRecipes[i] = e.Recipe;
            row.Icon = ItemIcons.Get(e.Recipe.Output);
            row.TooltipText = Describe(e.Recipe);
            row.AddThemeColorOverride("font_color", UiTheme.Rarity(e.Recipe.Output.Rarity));
        }
    }

    private void AddRow()
    {
        int index = _rows.Count;
        var row = new Button
        {
            ExpandIcon = false, Alignment = HorizontalAlignment.Left, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, RowHeight),
            FocusMode = FocusModeEnum.None, // Space jumps with the inventory open; a focused row would craft again
        };
        row.AddThemeConstantOverride("icon_max_width", 32);
        row.Pressed += () =>
        {
            if (index < _entries.Count) Craft(_entries[index].Recipe, Input.IsKeyPressed(Key.Shift));
        };
        _list.AddChild(row);
        _rows.Add(row);
        _rowRecipes.Add(null);
    }

    private void Craft(CompiledRecipe recipe, bool many)
    {
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        int times = many ? System.Math.Max(1, recipe.Output.MaxStack / System.Math.Max(1, recipe.Def.Count)) : 1;
        int crafted = 0;
        for (int i = 0; i < times && _ui.Recipes.Craft(recipe, _ui.Inventory, env.Stations, env.Context); i++) crafted++;
        if (crafted > 0) Audio.Sfx.Play("craft");
        if (crafted > 0) _ui.Notify($"Crafted {crafted * recipe.Def.Count} {recipe.Output.DisplayName}", UiTheme.Text);
        else _ui.Notify("Not enough room in the backpack", UiTheme.Life);
        Refresh();
    }

    private string Describe(CompiledRecipe r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Requires:");
        foreach (var ing in r.Inputs)
            sb.AppendLine($"  {ing.Count}x {(ing.Group != null ? $"any {ing.Group.Replace('_', ' ')}" : ing.Accepts.First().DisplayName)}");
        if (r.Def.Stations.Length > 0) sb.AppendLine("At: " + StationNames.Describe(r.Def.Stations, _ui.Game.Session.Content));
        return sb.ToString().TrimEnd();
    }
}
