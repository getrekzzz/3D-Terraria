using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Index;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// A recipe as the crafting column shows its selection (and the Guide's help slot can reuse): its name in its rarity
/// colour with the count it makes, 'Materials' as small slots holding each ingredient's count (red while the player has
/// too few; a group ingredient shows the kind the player carries most of and reads 'Any Wood' in its tooltip),
/// 'Required Objects: Iron Anvil or Lead Anvil' from <see cref="StationNames"/> and any condition ('Near Water').
/// <see cref="Show"/> builds it when the recipe changes; <see cref="UpdateCounts"/> recolours the counts in place.
/// Material slot views are pooled; the box takes no clicks itself.
/// </summary>
public sealed partial class RecipeDetail : VBoxContainer
{
    /// <summary>A material slot's side at slot scale 1.</summary>
    public const float MaterialSlot = 32;

    private readonly UiContext _ui;
    private readonly List<ItemSlotView> _materialViews = new();
    private readonly List<ItemStack> _materials = new();
    private readonly OutlinedText _name;
    private readonly OutlinedText _caption;
    private readonly HFlowContainer _row;
    private readonly Label _stations;
    private readonly Label _condition;
    private float _slotScale = 1f;

    public RecipeDetail(UiContext ui)
    {
        _ui = ui;
        Name = "RecipeDetail";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
        AddChild(_name = new OutlinedText { Align = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, UiLayout.TextLine) });
        AddChild(_caption = new OutlinedText
        {
            Text = "Materials", FontSize = UiFonts.Small, Align = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, UiFonts.Small + 3),
        });
        _row = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("h_separation", (int)UiLayout.Gap);
        _row.AddThemeConstantOverride("v_separation", (int)UiLayout.Gap);
        AddChild(_row);
        AddChild(_stations = TextLine(UiTheme.Text));
        AddChild(_condition = TextLine(UiTheme.Muted));
        Show(null);
    }

    /// <summary>The recipe shown, if any.</summary>
    public CompiledRecipe? Recipe { get; private set; }

    /// <summary>The line shown when there is no recipe.</summary>
    public string EmptyText { get; set; } = "Nothing to craft here";

    /// <summary>Material slots at the layout's slot scale.</summary>
    public void Arrange(float slotScale)
    {
        _slotScale = slotScale;
        float side = MaterialSlot * slotScale;
        foreach (var view in _materialViews) view.CustomMinimumSize = new Vector2(side, side);
    }

    /// <summary>Shows <paramref name="recipe"/> (null: the <see cref="EmptyText"/> line alone).</summary>
    public void Show(CompiledRecipe? recipe)
    {
        Recipe = recipe;
        _caption.Visible = _row.Visible = recipe != null;
        if (recipe == null)
        {
            _materials.Clear();
            _name.Text = EmptyText;
            _name.SelfModulate = UiTheme.Muted;
            _stations.Visible = _condition.Visible = false;
            return;
        }
        var content = _ui.Game.Session.Content;
        _name.Text = recipe.Def.Count > 1 ? $"{recipe.Output.DisplayName} ({recipe.Def.Count})" : recipe.Output.DisplayName;
        _name.SelfModulate = UiTheme.Rarity(recipe.Output.Rarity);
        _stations.Visible = recipe.Def.Stations.Length > 0;
        if (_stations.Visible) _stations.Text = "Required Objects: " + StationNames.Describe(recipe.Def.Stations, content);
        _condition.Text = RecipeText.Conditions(recipe.Def, content);
        _condition.Visible = _condition.Text.Length > 0;
        while (_materialViews.Count < recipe.Inputs.Length) AddMaterialView();
        for (int i = 0; i < _materialViews.Count; i++) _materialViews[i].Visible = i < recipe.Inputs.Length;
        UpdateCounts();
    }

    /// <summary>Each material's stack (a group's kind the player carries most of) and whether the player is short of it.</summary>
    public void UpdateCounts()
    {
        _materials.Clear();
        if (Recipe == null) return;
        var inputs = Recipe.Inputs;
        for (int i = 0; i < inputs.Length; i++)
        {
            var item = Displayed(inputs[i], out int have);
            _materials.Add(new ItemStack(item, inputs[i].Count));
            _materialViews[i].Short = have < inputs[i].Count;
        }
    }

    public override void _Process(double delta)
    {
        // Expert and Master names cycle their colours, as in the tooltip.
        if (Recipe is { Output.Rarity: < 0 } r && IsVisibleInTree()) _name.SelfModulate = UiTheme.Rarity(r.Output.Rarity);
    }

    /// <summary>The item a material slot shows: the ingredient's one item, or the accepted kind the player holds most of.</summary>
    private ItemDef Displayed(Ingredient ing, out int have)
    {
        var content = _ui.Game.Session.Content;
        var inv = _ui.Inventory;
        ItemDef? best = null;
        int bestCount = -1;
        have = 0;
        // A group in its own order, so the empty-handed default is its first item (Wood for Any Wood).
        var group = ing.Group != null ? content.RecipeGroups.Find(ing.Group) : null;
        if (group != null)
        {
            foreach (var id in group.Items) Weigh(content.Items.Get(id), inv, ref best, ref bestCount, ref have);
        }
        else
        {
            foreach (var item in ing.Accepts) Weigh(item, inv, ref best, ref bestCount, ref have);
        }
        return best!;
    }

    private static void Weigh(ItemDef item, PlayerInventory inv, ref ItemDef? best, ref int bestCount, ref int have)
    {
        int n = inv.CountAll(item);
        have += n;
        if (n <= bestCount) return;
        best = item;
        bestCount = n;
    }

    private void AddMaterialView()
    {
        int index = _materialViews.Count;
        float side = MaterialSlot * _slotScale;
        var view = new ItemSlotView(() => index < _materials.Count ? _materials[index] : ItemStack.Empty, side, SlotKind.Crafting, Tooltip(index));
        _row.AddChild(view);
        _materialViews.Add(view);
    }

    /// <summary>A material's tooltip: the item's own, named for its group when any kind will do ('Any Wood').</summary>
    private TooltipSource Tooltip(int index) => stack =>
    {
        var lines = _ui.Tooltip(stack);
        if (Recipe is { } r && index < r.Inputs.Length && r.Inputs[index].Group != null && lines.Count > 0)
            lines[0] = (RecipeText.Ingredient(r.Inputs[index], _ui.Game.Session.Content), lines[0].Kind);
        return lines;
    };

    private static Label TextLine(Color color)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore, SelfModulate = color };
        label.AddThemeFontSizeOverride("font_size", UiFonts.Small);
        return label;
    }
}
