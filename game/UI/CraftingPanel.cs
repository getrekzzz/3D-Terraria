using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Strata.Core.Gameplay.Crafting;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Crafting as Terraria shows it, in the layout's Crafting region at the bottom-left of the inventory screen: a column
/// of recipe slots scrolled with the mouse wheel, the selected one drawn larger with a gold rim, and beside it that
/// recipe's name, its materials as small slots (the count red when the player is short), 'Required Objects' in the
/// stations' display names and any condition (<see cref="RecipeDetail"/>). The column lists what can be crafted here;
/// the hammer toggle adds the known recipes the player cannot craft yet, greyed. Clicking a recipe selects it; clicking
/// the selected one crafts it (CraftingPanel.Recipe.cs). The 'Near:' line names the station objects and liquids in reach.
/// Nothing rebuilds on a timer: the backpack, coin and ammo slots mark the list dirty (rebuilt at most once a frame,
/// and only when <see cref="CraftingEnvironment.Signature"/> changed; otherwise the material counts update in place),
/// and a half-second poll of the stations and liquids in reach marks it dirty only when they differ, so a hovered
/// recipe's tooltip stays put. Slot views are pooled; the parts of the region without slots leave clicks to the world.
/// </summary>
public sealed partial class CraftingPanel : Control
{
    private const double PollSeconds = 0.5;
    private const float NearLine = UiFonts.Small + 3;
    private const float ToggleWidth = 24;

    private static readonly Predicate<CraftingEnvironment.Entry> Uncraftable = e => !e.CanCraft;

    private readonly List<ItemSlotView> _recipeViews = new();
    private readonly List<CraftingEnvironment.Entry> _shown = new();
    private readonly StringBuilder _nearText = new();
    private UiContext _ui = null!;
    private OutlinedText _title = null!;
    private TextureButton _showAllToggle = null!;
    private OutlinedText _near = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _column = null!;
    private RecipeDetail _detail = null!;
    private CraftingEnvironment? _env;
    private PlayerInventory? _watched;
    private float _slotScale = 1f;
    private ulong _signature;
    private bool _dirty = true;
    private bool _showAll;
    private double _poll;
    private int _selected = -1;
    private CompiledRecipe? _selectedRecipe;

    /// <summary>Recipe slots in the column (the <c>ui.recipes</c> probe).</summary>
    public int RecipesShown => _shown.Count;

    /// <summary>Times the column was rebuilt since the screen last opened (the <c>ui.recipe_rebuilds</c> probe).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>The selected recipe, crafted by a click on its slot.</summary>
    public CompiledRecipe? Selected => _selectedRecipe;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore; // the region's empty parts leave clicks to the world
        AddChild(_title = new OutlinedText { Text = "Crafting", Align = HorizontalAlignment.Left });
        _showAllToggle = new TextureButton
        {
            Name = "ShowAll", TextureNormal = UiSprites.Get("reforge_hammer"), ToggleMode = true, FocusMode = FocusModeEnum.None,
            TextureFilter = TextureFilterEnum.Nearest, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepCentered,
        };
        _showAllToggle.Toggled += ShowAll;
        ShowAll(false);
        AddChild(_showAllToggle);
        // The line may be cut short: its tooltip shows the whole of it.
        AddChild(_near = new OutlinedText
        {
            FontSize = UiFonts.Small, Align = HorizontalAlignment.Left, SelfModulate = UiTheme.Muted, MouseFilter = MouseFilterEnum.Pass,
        });
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            FollowFocus = false,
        };
        _column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", (int)UiLayout.Gap);
        _scroll.AddChild(_column);
        AddChild(_scroll);
        AddChild(_detail = new RecipeDetail(ui));
        Watch(ui.Inventory);
    }

    public override void _ExitTree()
    {
        if (_watched != null) Unwatch(_watched);
        _watched = null;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.inventory_ui");
        if (_ui.Inventory != _watched) Watch(_ui.Inventory);
        _poll -= delta;
        if (_poll <= 0) Poll();
        if (_dirty) Update();
    }

    /// <summary>The screen opened: the stations and liquids in reach are read again now, and the rebuild count restarts.</summary>
    public void Refresh()
    {
        Rebuilds = 0;
        Poll();
    }

    /// <summary>Lays the parts out in the region at the layout's slot scale (the HUD calls it when the layout changes).</summary>
    public void Arrange(float slotScale)
    {
        _slotScale = slotScale;
        float w = Size.X, top = UiLayout.TextLine;
        _title.Position = Vector2.Zero;
        _title.Size = new Vector2(w - ToggleWidth, top);
        _showAllToggle.Position = new Vector2(w - ToggleWidth, 0);
        _showAllToggle.Size = new Vector2(ToggleWidth, top);
        _near.Position = new Vector2(0, top);
        _near.Size = new Vector2(w, NearLine);
        float bodyY = top + NearLine + UiLayout.Gap, columnW = ColumnWidth;
        _scroll.Position = new Vector2(0, bodyY);
        _detail.Position = new Vector2(columnW + 2 * UiLayout.Gap, bodyY);
        _detail.Size = new Vector2(w - columnW - 2 * UiLayout.Gap, Size.Y - bodyY);
        _detail.Arrange(slotScale);
        SizeRecipeViews();
    }

    private float ColumnWidth => UiLayout.CraftSelectedScale * UiLayout.Slot * _slotScale;

    /// <summary>Reads the stations and liquids in reach; the list is marked dirty only when either set changed.</summary>
    private void Poll()
    {
        _poll = PollSeconds;
        var env = CraftingEnvironment.Around(_ui.Game, _ui.Entity);
        var last = _env;
        _env = env;
        if (last != null && env.Stations.SetEquals(last.Stations) && env.Context.NearbyLiquids.SetEquals(last.Context.NearbyLiquids))
        {
            // The same stations from other objects (a Lead Anvil for an Iron one) only rename the line.
            if (!env.ObjectNames.SequenceEqual(last.ObjectNames)) ShowNear(env);
            return;
        }
        _dirty = true;
        ShowNear(env);
    }

    /// <summary>'Near: Iron Anvil, Work Bench, Water': the station objects in reach by name, then the liquids.</summary>
    private void ShowNear(CraftingEnvironment env)
    {
        _nearText.Clear();
        foreach (var name in env.ObjectNames) _nearText.Append(_nearText.Length == 0 ? "Near: " : ", ").Append(name);
        foreach (var liquid in env.Context.NearbyLiquids)
            _nearText.Append(_nearText.Length == 0 ? "Near: " : ", ").Append(_ui.Game.Session.Content.Liquids.Find(liquid)?.DisplayName ?? liquid);
        _near.Text = _nearText.Length == 0 ? "No crafting stations nearby" : _nearText.ToString();
        _near.TooltipText = _near.Text;
    }

    /// <summary>Browses again; rebuilds the column only when the list's signature changed, else updates the material counts.</summary>
    private void Update()
    {
        _dirty = false;
        var entries = _env!.Browse(_ui.Recipes, _ui.Inventory);
        if (!_showAll) entries.RemoveAll(Uncraftable);
        ulong signature = CraftingEnvironment.Signature(entries);
        if (signature == _signature)
        {
            _detail.UpdateCounts();
            return;
        }
        _signature = signature;
        Rebuilds++;
        _shown.Clear();
        _shown.AddRange(entries);
        while (_recipeViews.Count < _shown.Count) AddRecipeView();
        for (int i = 0; i < _recipeViews.Count; i++)
        {
            var view = _recipeViews[i];
            view.Visible = i < _shown.Count;
            if (view.Visible) view.Dimmed = !_shown[i].CanCraft;
        }
        // The selected recipe keeps its selection wherever it moved; gone, the one now in its place is selected.
        int index = _selectedRecipe == null ? 0 : _shown.FindIndex(e => e.Recipe == _selectedRecipe);
        Select(index >= 0 ? index : _selected);
    }

    /// <summary>Selects the recipe at <paramref name="index"/> (clamped to the list): its slot grows, its details show.</summary>
    private void Select(int index)
    {
        _selected = _shown.Count == 0 ? -1 : Math.Clamp(index, 0, _shown.Count - 1);
        var recipe = _selected >= 0 ? _shown[_selected].Recipe : null;
        bool changed = recipe != _selectedRecipe;
        _selectedRecipe = recipe;
        SizeRecipeViews();
        if (!changed)
        {
            _detail.UpdateCounts();
            return;
        }
        // A hold crafting one recipe never runs on into another.
        _holding = null;
        foreach (var view in _recipeViews) view.StopRepeat();
        _detail.EmptyText = EmptyText;
        _detail.Show(recipe);
        if (_selected >= 0)
        {
            var selected = _recipeViews[_selected];
            Callable.From(() => _scroll.EnsureControlVisible(selected)).CallDeferred(); // after the column re-sorts
        }
    }

    /// <summary>Recipe slots at the slot size, the selected one 1.25 times larger and gold-rimmed; the column as tall as they are.</summary>
    private void SizeRecipeViews()
    {
        float side = UiLayout.Slot * _slotScale, big = ColumnWidth, height = 0;
        for (int i = 0; i < _shown.Count && i < _recipeViews.Count; i++)
        {
            var view = _recipeViews[i];
            bool selected = i == _selected;
            view.Kind = selected ? SlotKind.CraftingSelected : SlotKind.Crafting;
            float s = selected ? big : side;
            view.CustomMinimumSize = new Vector2(s, s);
            height += s + (i > 0 ? UiLayout.Gap : 0);
        }
        // Only as tall as the slots, so the empty part of the region below them takes no clicks.
        _scroll.Size = new Vector2(ColumnWidth, Mathf.Min(height, Mathf.Max(0, Size.Y - _scroll.Position.Y)));
    }

    private void AddRecipeView()
    {
        int index = _recipeViews.Count;
        var view = new ItemSlotView(() => index < _shown.Count ? Output(_shown[index].Recipe) : ItemStack.Empty, UiLayout.Slot * _slotScale,
            SlotKind.Crafting, _ui.Tooltip) { RepeatRight = true, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        view.Clicked += (button, mods) => RecipeClicked(view, index, button, mods);
        _column.AddChild(view);
        _recipeViews.Add(view);
    }

    private static ItemStack Output(CompiledRecipe r) => new(r.Output, r.Def.Count);

    /// <summary>Marks the list dirty on any change to the slots crafting draws from.</summary>
    private void Watch(PlayerInventory inventory)
    {
        if (_watched != null) Unwatch(_watched);
        _watched = inventory;
        inventory.Backpack.Changed += ItemsChanged;
        inventory.Coins.Changed += ItemsChanged;
        inventory.Ammo.Changed += ItemsChanged;
        _dirty = true;
    }

    private void Unwatch(PlayerInventory inventory)
    {
        inventory.Backpack.Changed -= ItemsChanged;
        inventory.Coins.Changed -= ItemsChanged;
        inventory.Ammo.Changed -= ItemsChanged;
    }

    private void ItemsChanged(int slot) => _dirty = true;

    private string EmptyText => _showAll ? "No known recipes here" : "Nothing to craft here";

    /// <summary>The hammer toggle: also list the known recipes the player cannot craft yet (greyed), or only the craftable ones.</summary>
    private void ShowAll(bool on)
    {
        _showAll = on;
        _showAllToggle.SelfModulate = on ? UiTheme.Gold : new Color(1, 1, 1, 0.6f);
        _showAllToggle.TooltipText = on ? "Showing every known recipe (click for craftable only)" : "Show every known recipe, uncraftable ones greyed";
        _dirty = true;
        if (_detail is not { Recipe: null }) return;
        _detail.EmptyText = EmptyText;
        _detail.Show(null);
    }
}
