using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.UI;

/// <summary>
/// In-game overlay: hotbar, life and mana, active buffs, crosshair, build-shape hint, pickup toasts and the
/// death screen. Hidden parts of it (hotbar) stay visible while the inventory is open, as in the reference game.
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private UiContext _ui = null!;
    private readonly List<ItemSlotView> _hotbar = new();
    private Label _heldName = null!;
    private float _heldNameTime;
    private ItemDef? _lastHeld;
    private ProgressBar _life = null!;
    private ProgressBar _mana = null!;
    private ProgressBar _breath = null!;
    private Label _breathText = null!;
    private Label _lifeText = null!;
    private Label _manaText = null!;
    private HBoxContainer _buffs = null!;
    private Label _buildHint = null!;
    private VBoxContainer _toasts = null!;
    private VBoxContainer _announcements = null!;
    private Label _death = null!;
    private Crosshair _crosshair = null!;
    private TargetLabel _targetName = null!;
    private Label _fps = null!;
    private Label _info = null!;
    private PanelContainer _bossPanel = null!;
    private Label _bossName = null!;
    private ProgressBar _bossBar = null!;

    /// <summary>What the crosshair label shows ("Blue Slime 25/25", a block's name, or empty).</summary>
    public string TargetText => _targetName.Text;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "Hud";
        Layer = 10;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var hotbar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hotbar.AddThemeConstantOverride("separation", 4);
        hotbar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        hotbar.GrowHorizontal = Control.GrowDirection.Both;
        hotbar.GrowVertical = Control.GrowDirection.Begin;
        hotbar.OffsetBottom = -14;
        for (int i = 0; i < PlayerInventory.HotbarSize; i++)
        {
            int slot = i;
            var view = new ItemSlotView(() => ui.Inventory.Backpack[slot], 58, ui.Tooltip) { Badge = ((i + 1) % 10).ToString() };
            view.Clicked += (_, _) => ui.Inventory.Select(slot);
            hotbar.AddChild(view);
            _hotbar.Add(view);
        }
        root.AddChild(hotbar);

        _heldName = Centered(root, -90, 18);
        _buildHint = Centered(root, -112, 13);
        _buildHint.AddThemeColorOverride("font_color", UiTheme.Muted);

        var vitals = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(260, 0) };
        vitals.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        vitals.GrowHorizontal = Control.GrowDirection.Begin;
        vitals.OffsetRight = -16;
        vitals.OffsetTop = 14;
        (_life, _lifeText) = Bar(vitals, UiTheme.Life);
        (_mana, _manaText) = Bar(vitals, UiTheme.Mana);
        (_breath, _breathText) = Bar(vitals, new Color(0.55f, 0.85f, 1f));
        _breath.MaxValue = Strata.Core.Session.PlayerEntity.MaxBreath;
        root.AddChild(vitals);

        _buffs = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(16, 14) };
        _buffs.AddThemeConstantOverride("separation", 6);
        root.AddChild(_buffs);

        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _toasts.GrowHorizontal = Control.GrowDirection.Begin;
        _toasts.OffsetRight = -24;
        root.AddChild(_toasts);
        _announcements = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _announcements.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _announcements.GrowHorizontal = Control.GrowDirection.Both;
        _announcements.OffsetTop = 120;
        root.AddChild(_announcements);

        _crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        root.AddChild(_crosshair);
        _targetName = new TargetLabel();
        _targetName.Initialize(ui);
        root.AddChild(_targetName);
        _fps = new Label { Position = new Vector2(16, 44), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fps.AddThemeFontSizeOverride("font_size", 14);
        root.AddChild(_fps);
        _info = new Label { Position = new Vector2(16, 64), MouseFilter = Control.MouseFilterEnum.Ignore };
        _info.AddThemeFontSizeOverride("font_size", 14);
        _info.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _info.AddThemeConstantOverride("outline_size", 4);
        root.AddChild(_info);

        _death = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _death.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _death.AddThemeFontSizeOverride("font_size", 44);
        _death.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
        root.AddChild(_death);

        _bossPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(520, 0) };
        _bossPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _bossPanel.GrowHorizontal = Control.GrowDirection.Both;
        _bossPanel.OffsetTop = 14;
        var bossBox = new VBoxContainer();
        _bossName = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _bossName.AddThemeFontSizeOverride("font_size", 18);
        bossBox.AddChild(_bossName);
        (_bossBar, _) = Bar(bossBox, new Color(0.75f, 0.12f, 0.2f));
        _bossBar.CustomMinimumSize = new Vector2(500, 18);
        _bossPanel.AddChild(bossBox);
        root.AddChild(_bossPanel);

        var npcs = ui.Game.Npcs;
        npcs.BossSpawned += b => Announce($"{b.Def.Boss!.Title} has awoken!", new Color(0.8f, 0.5f, 1f));
        npcs.BossDefeated += b => Announce($"{b.Def.Boss!.Title} has been defeated!", new Color(1f, 0.85f, 0.4f));
        npcs.BossLeft += b => Announce($"{b.Def.Boss!.Title} has left.", UiTheme.Muted);
        ui.Game.ItemUse.Refused += (p, message) =>
        {
            if (p == ui.Entity) Toast(message, UiTheme.Muted);
        };
        ui.Notified += Toast;
        ui.Game.Town.Arrived += t => Announce($"{t.Title} has arrived!", new Color(0.55f, 0.9f, 0.6f));
        ui.Game.Town.HomeLost += t => Toast($"{t.Title}'s home is no longer suitable.", UiTheme.Muted);
        ui.Game.Town.Died += t => Announce($"{t.Title} was slain...", new Color(1f, 0.4f, 0.35f));
        ui.Game.Town.Departed += t => Announce($"{t.Title} has left.", new Color(0.7f, 0.8f, 1f));
        ui.Game.Progression.Raised += f =>
        {
            if (f.Announce != null) Announce(f.Announce, new Color(0.75f, 0.55f, 1f));
        };
        ui.Game.Objectives.Announced += (text, color) => Announce(text, Color.FromHtml(color));
        npcs.Announced += text => Announce(text, new Color(1f, 0.55f, 0.45f));
        ui.Game.Interaction.Refused += (p, message) =>
        {
            if (p == ui.Entity) Toast(message, UiTheme.Muted);
        };
        ui.Game.ItemUse.Caught += (p, stack) =>
        {
            if (p == ui.Entity) Toast($"Caught {stack.Item!.DisplayName}!", UiTheme.Rarity(stack.Item.Rarity));
        };
        ui.Game.Events.Announced += (text, def) => Announce(text, def.SkyTint != null ? Color.FromHtml(def.SkyTint).Lightened(0.3f) : new Color(0.7f, 0.85f, 1f));
        ui.Game.ItemPickedUp += (p, stack) =>
        {
            if (p == ui.Entity) Toast($"+{stack.Count} {stack.Item!.DisplayName}", UiTheme.Rarity(stack.Item.Rarity));
        };
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.hud");
        var inv = _ui.Inventory;
        for (int i = 0; i < _hotbar.Count; i++)
        {
            bool sel = inv.SelectedHotbar == i;
            if (_hotbar[i].Selected != sel)
            {
                _hotbar[i].Selected = sel;
                _hotbar[i].QueueRedraw();
            }
        }
        var held = inv.Held.Item;
        if (held != _lastHeld)
        {
            _lastHeld = held;
            _heldName.Text = held?.DisplayName ?? "";
            if (held != null) _heldName.AddThemeColorOverride("font_color", UiTheme.Rarity(held.Rarity));
            _heldNameTime = 2.5f;
        }
        _heldNameTime -= (float)delta;
        _heldName.Modulate = new Color(1, 1, 1, Mathf.Clamp(_heldNameTime, 0, 1));
        var info = held != null ? _ui.Game.Uses[held] : null;
        _buildHint.Text = info?.Block is { Render: BlockRender.Crisp }
            ? $"{_ui.Entity.BuildShape} ({(_ui.Entity.FacingQuarter + _ui.Entity.BuildRotation) & 3}) — T shape, R rotate"
            : "";

        var v = _ui.Entity.State.Vitals;
        _life.MaxValue = v.MaxLife;
        _life.Value = v.Life;
        _lifeText.Text = $"Life {v.Life}/{v.MaxLife}";
        _mana.MaxValue = v.MaxMana;
        _mana.Value = v.Mana;
        _manaText.Text = $"Mana {v.Mana}/{v.MaxMana}";
        int breath = _ui.Entity.Breath;
        _breath.Visible = breath < Strata.Core.Session.PlayerEntity.MaxBreath;
        _breath.Value = breath;
        _breathText.Text = breath > 0 ? $"Breath {breath * 100 / Strata.Core.Session.PlayerEntity.MaxBreath}%" : "Drowning!";
        UpdateBuffs();
        UpdateBossBar();
        _crosshair.InReach = _ui.Interaction.InReach;
        _fps.Visible = Settings.UserSettings.Current.ShowFps;
        if (_fps.Visible) _fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";
        _info.Text = InfoText();
        _death.Visible = !_ui.Entity.Alive;
        if (_death.Visible)
            _death.Text = _ui.Entity.Lost
                ? $"{_ui.Entity.State.Name} has perished.\nHardcore characters do not return."
                : $"You were slain\nRespawning in {Mathf.CeilToInt(_ui.Entity.RespawnTicks / 60f)}";
    }

    private void UpdateBossBar()
    {
        Strata.Core.Gameplay.Entities.Npc? boss = null;
        foreach (var n in _ui.Game.Npcs.All)
            if (n.IsBoss && n.Alive && (boss == null || n.Life > boss.Life)) boss = n;
        _bossPanel.Visible = boss != null;
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

    private void UpdateBuffs()
    {
        var active = _ui.Entity.State.Buffs.Active;
        while (_buffs.GetChildCount() > active.Count) _buffs.GetChild(_buffs.GetChildCount() - 1).Free();
        while (_buffs.GetChildCount() < active.Count)
        {
            var panel = new PanelContainer();
            panel.AddChild(new Label());
            _buffs.AddChild(panel);
        }
        for (int i = 0; i < active.Count; i++)
        {
            var b = active[i];
            var label = _buffs.GetChild(i).GetChild<Label>(0);
            label.Text = $"{b.Def.DisplayName} {Mathf.CeilToInt(b.TicksLeft / 60f)}s";
            label.AddThemeColorOverride("font_color", Color.FromHtml(b.Def.Color));
            label.TooltipText = b.Def.Description ?? "";
        }
    }

    public void Toast(string text, Color color)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right };
        label.AddThemeColorOverride("font_color", color);
        _toasts.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenInterval(2.2);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(label.QueueFree));
        while (_toasts.GetChildCount() > 6) _toasts.GetChild(0).Free();
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

    private static Label Centered(Control root, float offsetBottom, int size)
    {
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        label.GrowHorizontal = Control.GrowDirection.Both;
        label.GrowVertical = Control.GrowDirection.Begin;
        label.OffsetBottom = offsetBottom;
        label.AddThemeFontSizeOverride("font_size", size);
        root.AddChild(label);
        return label;
    }

    private static (ProgressBar, Label) Bar(Container parent, Color color)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(260, 22), MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.55f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.AddThemeFontSizeOverride("font_size", 13);
        bar.AddChild(label);
        parent.AddChild(bar);
        return (bar, label);
    }

    /// <summary>Small cross at screen centre; brighter when the target is within reach.</summary>
    private sealed partial class Crosshair : Control
    {
        private bool _inReach;

        public bool InReach
        {
            set
            {
                if (_inReach == value) return;
                _inReach = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            var c = _inReach ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.45f);
            DrawLine(new Vector2(-8, 0), new Vector2(-3, 0), c, 2);
            DrawLine(new Vector2(3, 0), new Vector2(8, 0), c, 2);
            DrawLine(new Vector2(0, -8), new Vector2(0, -3), c, 2);
            DrawLine(new Vector2(0, 3), new Vector2(0, 8), c, 2);
        }
    }

    /// <summary>Readouts from worn information accessories (Depth Meter, Compass, GPS, Cell Phone).</summary>
    private string InfoText()
    {
        var info = _ui.Entity.State.Effects.Info;
        if (info.Count == 0) return "";
        var session = _ui.Game.Session;
        var cell = _ui.Entity.FeetCell;
        var lines = new System.Collections.Generic.List<string>();
        if (info.Contains("depth"))
        {
            float depth = session.ContextAt(cell).Depth * Strata.Core.Common.Units.CellSize;
            lines.Add(Mathf.Abs(depth) < 2f ? "Depth: surface" : depth > 0 ? $"Depth: {depth:0} m below" : $"Height: {-depth:0} m above");
        }
        if (info.Contains("position"))
        {
            var size = session.Settings.Dimensions;
            float east = (cell.X - size.X * 0.5f) * Strata.Core.Common.Units.CellSize, north = (size.Z * 0.5f - cell.Z) * Strata.Core.Common.Units.CellSize;
            lines.Add($"{Mathf.Abs(east):0} m {(east >= 0 ? "east" : "west")}, {Mathf.Abs(north):0} m {(north >= 0 ? "north" : "south")}");
        }
        if (info.Contains("time"))
        {
            float hour = session.Clock.Hour;
            int h = (int)hour, m = (int)((hour - h) * 60);
            lines.Add($"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}");
        }
        return string.Join("\n", lines);
    }
}
