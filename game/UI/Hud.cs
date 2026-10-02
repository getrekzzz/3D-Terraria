using System;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The in-game overlay on the <see cref="UiLayout"/> model, laid out like Terraria's: the selected item's name and the
/// hotbar top-left with the buff row under them, life and mana top-right above the minimap and the info panel, the
/// crosshair with its interaction hint and target name at the centre, the breath bar above it and the status line under
/// it, the pickup feed bottom-left, the boss bar bottom-centre, event banners and the death screen. <see cref="ApplyLayout"/>
/// places every widget (and, through <see cref="LayoutApplied"/>, the minimap and the F3 overlay) when the window, the UI
/// scale or what is shown changes (the inventory and its panel and accessory rows, an NPC chat, a boss, the buff count,
/// the info rows, the minimap setting, the F3 overlay), never per frame; the inventory screen's parts take their regions
/// from it too.
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private const int FeedLines = 6;

    private UiContext _ui = null!;
    private ItemNameLabel _itemName = null!;
    private HotbarBar _hotbar = null!;
    private VitalsBars _vitals = null!;
    private Crosshair _crosshair = null!;
    private StatusLine _status = null!;
    private InfoPanel _info = null!;
    private Control _feed = null!;
    private VBoxContainer _feedLines = null!;
    private VBoxContainer _announcements = null!;
    private Label _death = null!;
    private PanelContainer _bossPanel = null!;
    private Label _bossName = null!;
    private ProgressBar _bossBar = null!;
    private InventoryScreen? _inventory;
    private TownDialog? _dialog;
    private PerfOverlay? _perf;
    private LayoutKey _key;
    private bool _bossShown, _bossPlaced;
    private (ItemDef? Item, Strata.Core.Voxels.CellShape Shape, int Turn) _hintFor;

    /// <summary>What decides the layout; a change re-runs <see cref="ApplyLayout"/>.</summary>
    private readonly record struct LayoutKey(Vector2 Size, bool Inventory, UiPanel Panel, int AccessoryRows, bool Chat, bool Boss, int Buffs,
        bool Minimap, int Info, bool Debug);

    /// <summary>The layout in effect (null until the first frame).</summary>
    public UiLayoutResult? Layout { get; private set; }

    /// <summary>The layout was recomputed: the widgets on other canvas layers (minimap, F3 overlay) move with it.</summary>
    public event Action<UiLayoutResult>? LayoutApplied;

    /// <summary>What the crosshair label shows ("Blue Slime 25/25", a block's name, or empty).</summary>
    public string TargetText => _crosshair.TargetText;

    /// <summary>The line above the hotbar: the held item's name, or "Inventory" (the <c>hud.item_name</c> probe).</summary>
    public string ItemName => _itemName.Text;

    /// <summary>The message on the status line under the crosshair, or "".</summary>
    public string Status => _status.Message;

    /// <summary>Active buffs shown with their generated icon (not the letter fallback); the hud.buff_icons probe.</summary>
    public int BuffIconsShown { get; private set; }

    /// <summary>The hotbar slot view at <paramref name="index"/> (the <c>ui hover hotbar</c> command), or null.</summary>
    public ItemSlotView? HotbarSlot(int index) => _hotbar.Slot(index);

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "Hud";
        Layer = 10;
        UiSprites.Preload();
        StatusIcons.Preload(ui.Game.Session.Content);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(_itemName = LayoutWidgets.Register(new ItemNameLabel(ui)));
        root.AddChild(_hotbar = LayoutWidgets.Register(new HotbarBar(ui)));
        root.AddChild(_buffs = LayoutWidgets.Register(new Control { Name = "Buffs", MouseFilter = Control.MouseFilterEnum.Ignore }));
        _vitals = new VitalsBars(ui, root);
        root.AddChild(_crosshair = LayoutWidgets.Register(new Crosshair(ui)));
        root.AddChild(_status = LayoutWidgets.Register(new StatusLine()));
        root.AddChild(_info = LayoutWidgets.Register(new InfoPanel(ui)));
        _feed = LayoutWidgets.Register(new Control { Name = "Feed", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true });
        _feedLines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, GrowVertical = Control.GrowDirection.Begin };
        _feedLines.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _feed.AddChild(_feedLines);
        root.AddChild(_feed);

        _announcements = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _announcements.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _announcements.GrowHorizontal = Control.GrowDirection.Both;
        _announcements.OffsetTop = 120;
        root.AddChild(_announcements);

        _death = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _death.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _death.AddThemeFontSizeOverride("font_size", 44);
        _death.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
        root.AddChild(_death);

        _bossPanel = LayoutWidgets.Register(new PanelContainer { Name = "BossBar", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore });
        var bossBox = new VBoxContainer();
        _bossName = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
        _bossName.AddThemeFontSizeOverride("font_size", 18);
        bossBox.AddChild(_bossName);
        _bossBar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        _bossBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.55f) });
        _bossBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.75f, 0.12f, 0.2f) });
        bossBox.AddChild(_bossBar);
        _bossPanel.AddChild(bossBox);
        root.AddChild(_bossPanel);
        WireMessages(ui);
    }

    /// <summary>
    /// The screens and layers placed from this layout that are built after the HUD: their state (inventory open, NPC
    /// chat, F3 overlay) feeds it, and the minimap and the overlay take their regions from it.
    /// </summary>
    public void Connect(InventoryScreen inventory, TownDialog dialog, Minimap minimap, PerfOverlay perf)
    {
        _inventory = inventory;
        _dialog = dialog;
        _perf = perf;
        LayoutApplied += inventory.Place;
        LayoutApplied += minimap.Place;
        LayoutApplied += perf.Place;
        // Opening the inventory or a chest lays the screen out at once, so no frame shows it unplaced.
        inventory.LayoutChanged += UpdateLayout;
        _key = default; // laid out again on the next frame, with them
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.hud");
        UpdateBossBar();
        UpdateLayout();
        _vitals.Update();
        UpdateBuffs();
        UpdateBuildHint();
        _death.Visible = !_ui.Entity.Alive;
        if (_death.Visible)
            _death.Text = _ui.Entity.Lost
                ? $"{_ui.Entity.State.Name} has perished.\nHardcore characters do not return."
                : $"You were slain\nRespawning in {Mathf.CeilToInt(_ui.Entity.RespawnTicks / 60f)}";
    }

    /// <summary>Lays the screen out again when what decides the layout changed (the window, the UI scale or what is shown).</summary>
    private void UpdateLayout()
    {
        bool inventory = _inventory?.IsOpen == true;
        var key = new LayoutKey(LayoutWidgets.LogicalSize(GetViewport()), inventory, inventory ? _inventory!.Panel : UiPanel.None,
            _inventory?.AccessoryRows ?? Strata.Core.Gameplay.Items.PlayerInventory.BaseAccessorySlots, _dialog?.IsOpen == true, _bossShown,
            _ui.Entity.State.Buffs.Active.Count, Settings.UserSettings.Current.ShowMinimap, _info.Lines, _perf?.IsOpen == true);
        if (!key.Equals(_key)) ApplyLayout(key);
    }

    /// <summary>Computes the layout for <paramref name="key"/> and moves every widget onto its region (hidden without one).</summary>
    private void ApplyLayout(LayoutKey key)
    {
        _key = key;
        var state = new UiState(InventoryOpen: key.Inventory, Panel: key.Panel, BossBar: key.Boss, NpcChat: key.Chat, BuffCount: key.Buffs,
            AccessoryRows: key.AccessoryRows, ShowMinimap: key.Minimap, InfoLines: key.Info, DebugOverlay: key.Debug);
        var layout = UiLayout.Compute(key.Size.X, key.Size.Y, state);
        Layout = layout;
        _itemName.InventoryOpen = key.Inventory;
        _itemName.Visible = LayoutWidgets.Place(_itemName, layout, UiRegion.ItemName);
        _hotbar.Visible = LayoutWidgets.Place(_hotbar, layout, UiRegion.Hotbar);
        _buffs.Visible = LayoutWidgets.Place(_buffs, layout, UiRegion.Buffs);
        _vitals.Place(layout);
        _crosshair.Place(layout, key.Size);
        _status.Visible = LayoutWidgets.Place(_status, layout, UiRegion.StatusLine);
        _info.Visible = LayoutWidgets.Place(_info, layout, UiRegion.Info);
        _feed.Visible = LayoutWidgets.Place(_feed, layout, UiRegion.Chat);
        _bossPlaced = LayoutWidgets.Place(_bossPanel, layout, UiRegion.BossBar);
        _bossPanel.Visible = _bossPlaced && _bossShown;
        ArrangeBuffs();
        LayoutApplied?.Invoke(layout);
    }

    private void WireMessages(UiContext ui)
    {
        var npcs = ui.Game.Npcs;
        npcs.BossSpawned += b => Announce($"{b.Def.Boss!.Title} has awoken!", new Color(0.8f, 0.5f, 1f));
        npcs.BossDefeated += b => Announce($"{b.Def.Boss!.Title} has been defeated!", new Color(1f, 0.85f, 0.4f));
        npcs.BossLeft += b => Announce($"{b.Def.Boss!.Title} has left.", UiTheme.Muted);
        ui.Game.Town.Arrived += t => Announce($"{t.Title} has arrived!", new Color(0.55f, 0.9f, 0.6f));
        ui.Game.Town.Died += t => Announce($"{t.Title} was slain...", new Color(1f, 0.4f, 0.35f));
        ui.Game.Town.Departed += t => Announce($"{t.Title} has left.", new Color(0.7f, 0.8f, 1f));
        ui.Game.Progression.Raised += f =>
        {
            if (f.Announce != null) Announce(f.Announce, new Color(0.75f, 0.55f, 1f));
        };
        ui.Game.Objectives.Announced += (text, color) => Announce(text, Color.FromHtml(color));
        npcs.Announced += text => Announce(text, new Color(1f, 0.55f, 0.45f));
        ui.Game.Events.Announced += (text, def) => Announce(text, def.SkyTint != null ? Color.FromHtml(def.SkyTint).Lightened(0.3f) : new Color(0.7f, 0.85f, 1f));

        // Refusals and notices: one line under the crosshair.
        ui.Game.ItemUse.Refused += (p, message) =>
        {
            if (p == ui.Entity) _status.Show(message, UiTheme.Muted);
        };
        ui.Game.Interaction.Refused += (p, message) =>
        {
            if (p == ui.Entity) _status.Show(message, UiTheme.Muted);
        };
        ui.Notified += _status.Show;
        ui.Game.Town.HomeLost += t => _status.Show($"{t.Title}'s home is no longer suitable.", UiTheme.Muted);

        // Pickups and catches: the feed in the chat corner.
        ui.Game.ItemUse.Caught += (p, stack) =>
        {
            if (p == ui.Entity) Toast($"Caught {stack.Item!.DisplayName}!", UiTheme.Rarity(stack.Item.Rarity));
        };
        ui.Game.ItemPickedUp += (p, stack) =>
        {
            if (p == ui.Entity) Toast($"+{stack.Count} {stack.Item!.DisplayName}", UiTheme.Rarity(stack.Item.Rarity));
        };
    }

    /// <summary>The build-shape hint for a held shaped block, rebuilt only when the block, shape or rotation changes.</summary>
    private void UpdateBuildHint()
    {
        var held = _ui.Inventory.Held.Item;
        var e = _ui.Entity;
        (ItemDef? Item, Strata.Core.Voxels.CellShape Shape, int Turn) at = (held, e.BuildShape, (e.FacingQuarter + e.BuildRotation) & 3);
        if (at == _hintFor) return;
        _hintFor = at;
        var info = held != null ? _ui.Game.Uses[held] : null;
        _status.Hint = info?.Block is { Render: BlockRender.Crisp } ? $"{at.Shape} ({at.Turn}) — T shape, R rotate" : "";
    }

    private void UpdateBossBar()
    {
        Strata.Core.Gameplay.Entities.Npc? boss = null;
        foreach (var n in _ui.Game.Npcs.All)
            if (n.IsBoss && n.Alive && (boss == null || n.Life > boss.Life)) boss = n;
        _bossShown = boss != null;
        _bossPanel.Visible = _bossPlaced && _bossShown;
        if (boss == null) return;
        // A pair of bosses (companions share the defeat flag) shows as one bar with their combined life.
        var flag = boss.Def.Boss!.Flag;
        long life = 0, max = 0;
        foreach (var n in _ui.Game.Npcs.All)
            if (n.Alive && (n == boss || (flag != null && n.Def.Boss?.Flag == flag)))
            {
                life += n.Life;
                max += n.MaxLife;
            }
        _bossName.Text = boss.Def.Boss.Title;
        _bossBar.MaxValue = max;
        _bossBar.Value = life;
    }

    /// <summary>A line in the feed at the bottom-left (pickups, catches) that fades after a couple of seconds.</summary>
    public void Toast(string text, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", UiFonts.OutlineColor);
        label.AddThemeConstantOverride("outline_size", UiFonts.Outline);
        _feedLines.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenInterval(2.2);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(label.QueueFree));
        while (_feedLines.GetChildCount() > FeedLines) _feedLines.GetChild(0).Free();
    }

    /// <summary>Large centred message (events, bosses) that fades after a few seconds.</summary>
    public void Announce(string text, Color color)
    {
        // Messages stack downwards instead of printing over one another.
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", color);
        _announcements.AddChild(label);
        while (_announcements.GetChildCount() > 4) _announcements.GetChild(0).Free();
        var tween = label.CreateTween();
        tween.TweenInterval(4.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 1.5);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }
}
