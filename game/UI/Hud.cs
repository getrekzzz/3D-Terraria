using System;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The in-game overlay on the <see cref="UiLayout"/> model, laid out like Terraria's: the selected item's name and the
/// hotbar top-left with the buff row under them, the hearts and mana stars top-right above the minimap and the info
/// panel, the crosshair with its interaction hint and target name at the centre, the breath bubbles above it and the
/// status line under it, the chat log bottom-left (Hud.Messages), pickup popups over the player's head, the boss bar
/// bottom-centre, an invasion's progress bottom-right and the death screen.
/// <see cref="ApplyLayout"/> places every widget (and, through <see cref="LayoutApplied"/>, the minimap and the F3
/// overlay) when the window, the UI scale or what is shown changes (the inventory and its panel and accessory rows, an
/// NPC chat, a boss, an invasion, the buff count, the info rows, the minimap setting, the F3 overlay), never per frame; the inventory
/// screen's parts take their regions from it too.
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private UiContext _ui = null!;
    private ItemNameLabel _itemName = null!;
    private HotbarBar _hotbar = null!;
    private BuffBar _buffBar = null!;
    private Crosshair _crosshair = null!;
    private StatusLine _status = null!;
    private InfoPanel _info = null!;
    private ChatLog _chat = null!;
    private PickupPopups _popups = null!;
    private DeathScreen _death = null!;
    private BossBar _bossBar = null!;
    private EventProgressPanel _eventBar = null!;
    private InventoryScreen? _inventory;
    private TownDialog? _dialog;
    private PerfOverlay? _perf;
    private LayoutKey _key;
    private (ItemDef? Item, Strata.Core.Voxels.CellShape Shape, int Turn) _hintFor;

    /// <summary>What decides the layout; a change re-runs <see cref="ApplyLayout"/>.</summary>
    private readonly record struct LayoutKey(Vector2 Size, bool Inventory, UiPanel Panel, int AccessoryRows, bool Chat, bool Boss, bool Event,
        int Buffs, bool Minimap, int Info, bool Debug);

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
    public int BuffIconsShown => _buffBar.IconsShown;

    /// <summary>The buff bar (the <c>ui buff cancel</c> command and the <c>hud.buffs</c> probe).</summary>
    public BuffBar Buffs => _buffBar;

    /// <summary>The info panel under the minimap (the Guide book hides it while open).</summary>
    public InfoPanel Info => _info;

    /// <summary>The chat log (the <c>ui chat</c> command and the <c>ui.chat_lines</c> probe).</summary>
    public ChatLog Chat => _chat;

    /// <summary>Pickup popups on screen over the player's head (the <c>ui.popups</c> probe).</summary>
    public int PopupsShown => _popups.Shown;

    /// <summary>The death screen (the <c>ui.death_dim</c> probe).</summary>
    public DeathScreen Death => _death;

    /// <summary>The boss bar (the <c>ui.bossbar</c> probe).</summary>
    public BossBar BossBar => _bossBar;

    /// <summary>The invasion progress panel (the <c>ui.eventbar</c> probe).</summary>
    public EventProgressPanel EventBar => _eventBar;

    /// <summary>The hotbar slot view at <paramref name="index"/> (the <c>ui hover hotbar</c> command), or null.</summary>
    public ItemSlotView? HotbarSlot(int index) => _hotbar.Slot(index);

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "Hud";
        Layer = 10;
        UiSprites.Preload();
        StatusIcons.Preload(ui.Game.Session.Content);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(_itemName = LayoutWidgets.Register(new ItemNameLabel(ui)));
        root.AddChild(_hotbar = LayoutWidgets.Register(new HotbarBar(ui)));
        root.AddChild(_buffBar = LayoutWidgets.Register(new BuffBar(ui)));
        BuildVitals(root);
        root.AddChild(_crosshair = LayoutWidgets.Register(new Crosshair(ui)));
        root.AddChild(_status = LayoutWidgets.Register(new StatusLine()));
        root.AddChild(_info = LayoutWidgets.Register(new InfoPanel(ui)));
        root.AddChild(_chat = LayoutWidgets.Register(new ChatLog()));
        root.AddChild(_popups = new PickupPopups(ui)); // full screen, anchored to the player: not a layout region
        BuildBanner(root);

        root.AddChild(_bossBar = LayoutWidgets.Register(new BossBar()));
        root.AddChild(_eventBar = LayoutWidgets.Register(new EventProgressPanel(ui.Game.Events)));
        // Boss head icons load in the background now, so a boss's arrival never waits on the disk.
        var bossIcons = new System.Collections.Generic.List<string>();
        foreach (var e in ui.Game.Session.Content.Enemies.All)
            if (e.Boss is { Minor: false }) bossIcons.Add(Strata.Core.Content.AssetPaths.BossIcon(e));
        Strata.Game.Items.PortraitIcons.Preload(bossIcons);
        root.AddChild(_death = new DeathScreen()); // last: over every widget
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
        LayoutApplied += dialog.Place;
        // Opening the inventory, a chest or the NPC chat box lays the screen out at once, so no frame shows it unplaced.
        inventory.LayoutChanged += UpdateLayout;
        dialog.LayoutChanged += UpdateLayout;
        _key = default; // laid out again on the next frame, with them
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.hud");
        Strata.Game.Items.PortraitIcons.Pump();
        _bossBar.Refresh(BossBarModel.Compute(_ui.Game.Npcs.All), delta); // before the layout, which reads what is shown
        _eventBar.Refresh(_ui.Game.Events, delta);
        _buffBar.Refresh(delta);
        UpdateLayout();
        UpdateVitals(delta);
        UpdateBuildHint();
        _death.Refresh(_ui.Entity, delta);
    }

    /// <summary>Lays the screen out again when what decides the layout changed (the window, the UI scale or what is shown).</summary>
    private void UpdateLayout()
    {
        bool inventory = _inventory?.IsOpen == true;
        var key = new LayoutKey(LayoutWidgets.LogicalSize(GetViewport()), inventory, inventory ? _inventory!.Panel : UiPanel.None,
            _inventory?.AccessoryRows ?? Strata.Core.Gameplay.Items.PlayerInventory.BaseAccessorySlots, _dialog?.IsOpen == true, _bossBar.Active,
            _eventBar.Active, _buffBar.Count, Settings.UserSettings.Current.ShowMinimap, _info.Lines, _perf?.IsOpen == true);
        if (!key.Equals(_key)) ApplyLayout(key);
    }

    /// <summary>Computes the layout for <paramref name="key"/> and moves every widget onto its region (hidden without one).</summary>
    private void ApplyLayout(LayoutKey key)
    {
        _key = key;
        var state = new UiState(InventoryOpen: key.Inventory, Panel: key.Panel, BossBar: key.Boss, EventBar: key.Event, NpcChat: key.Chat, BuffCount: key.Buffs,
            AccessoryRows: key.AccessoryRows, ShowMinimap: key.Minimap, InfoLines: key.Info, DebugOverlay: key.Debug);
        var layout = UiLayout.Compute(key.Size.X, key.Size.Y, state);
        Layout = layout;
        _itemName.InventoryOpen = key.Inventory;
        _itemName.Visible = LayoutWidgets.Place(_itemName, layout, UiRegion.ItemName);
        _hotbar.Visible = LayoutWidgets.Place(_hotbar, layout, UiRegion.Hotbar);
        _buffBar.Visible = LayoutWidgets.Place(_buffBar, layout, UiRegion.Buffs);
        PlaceVitals(layout);
        _crosshair.Place(layout, key.Size);
        _status.Visible = LayoutWidgets.Place(_status, layout, UiRegion.StatusLine);
        _info.Visible = LayoutWidgets.Place(_info, layout, UiRegion.Info);
        _chat.Visible = LayoutWidgets.Place(_chat, layout, UiRegion.Chat);
        _bossBar.Visible = LayoutWidgets.Place(_bossBar, layout, UiRegion.BossBar);
        _eventBar.Visible = LayoutWidgets.Place(_eventBar, layout, UiRegion.EventBar);
        _buffBar.Arrange();
        LayoutApplied?.Invoke(layout);
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
}
