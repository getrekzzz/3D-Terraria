using System;
using System.Linq;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Town;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// A resident's service in the inventory's panel host, as Terraria shows the Goblin Tinkerer's reforge: their name over
/// one slot bound to <see cref="PlayerInventory.Service"/> (it takes only what the <see cref="ServiceMode"/> works on:
/// <see cref="SlotFilters"/> refuses anything that cannot carry a prefix), with 'Place an item here to reforge' under it
/// while it is empty, and the reforge hammer beside it with 'Cost:' in coin colours under it once it holds an item.
/// Each press of the hammer reforges the slot (<c>TownServices.Reforge</c> on the service slot, paid from the purse):
/// the anvil clangs (<c>SoundEvents</c> plays <c>reforge</c> on <see cref="TownServices.Reforged"/>), the slot pops
/// from 1.1× back to its size and the tooltip is read again at once, so the new prefix shows under the pointer resting
/// on the hammer (which shows the item's tooltip) or the slot. A refusal goes to the status line. The
/// resident waits while the panel is open; it closes when they are out of talking range, and putting it away stows the
/// item (<see cref="StowSlot"/>) and ends the conversation. Another service (the Guide's help slot with the crafting
/// column's <see cref="RecipeDetail"/>) adds a <see cref="ServiceMode"/>, its hint and its own content beside the slot.
/// </summary>
public sealed partial class ServicePanel : Control
{
    private const double CostRefreshSeconds = 1; // the price follows the resident's mood
    private const float HoverScale = 1.1f;
    private const double HoverSeconds = 0.08;
    private const float PopScale = 1.1f;
    private const double PopSeconds = 0.15;

    private readonly UiContext _ui;
    private readonly ItemSlotView _slot;
    private readonly OutlinedText _title;
    private readonly OutlinedText _hint;
    private readonly CoinText _cost;
    private readonly TextureButton _hammer;
    private ItemStack _costFor;
    private double _costWait;
    private Tween? _pop;

    /// <param name="slot">The view of the service slot, made by the screen (its clicks go to <c>GameWorld.ClickSlot</c>).</param>
    public ServicePanel(UiContext ui, ItemSlotView slot)
    {
        _ui = ui;
        Name = "ServicePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_title = new OutlinedText { Align = HorizontalAlignment.Left });
        _slot = slot;
        AddChild(_slot);
        _hammer = new TextureButton
        {
            Name = "ReforgeHammer", TextureNormal = UiSprites.Get("reforge_hammer"), FocusMode = FocusModeEnum.None,
            TextureFilter = TextureFilterEnum.Nearest, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.KeepCentered,
            Visible = false,
        };
        _hammer.MouseEntered += () =>
        {
            Grow(_hammer, HoverScale);
            Audio.Sfx.Play("ui_hover");
            ShowTooltip(_hammer);
        };
        _hammer.MouseExited += () =>
        {
            Grow(_hammer, 1f);
            TooltipFollower.Current?.Hide(_hammer);
        };
        _hammer.Pressed += () => Hit();
        AddChild(_hammer);
        AddChild(_hint = new OutlinedText { Align = HorizontalAlignment.Left });
        AddChild(_cost = new CoinText { Label = "Cost:", Visible = false });
    }

    /// <summary>The resident whose service is shown, if any.</summary>
    public TownNpc? Npc { get; private set; }

    public ServiceMode Mode { get; private set; }

    /// <summary>The cost shown under the slot in copper; 0 while it is hidden (the <c>ui.service_cost</c> probe).</summary>
    public long CostShown => _cost.Visible ? _cost.Copper : 0;

    /// <summary>Reforges made with the hammer this session (the <c>ui.reforges</c> probe).</summary>
    public int Reforges { get; private set; }

    /// <summary>The resident went out of talking range or is gone: the screen closes the panel.</summary>
    public event Action? CloseRequested;

    /// <summary>Shows <paramref name="npc"/>'s service in <paramref name="mode"/> (the screen puts any other panel away first).</summary>
    public void Open(TownNpc npc, ServiceMode mode)
    {
        Npc = npc;
        Mode = mode;
        _ui.Inventory.ServiceMode = mode;
        _title.Text = npc.Title;
        _hint.Text = mode switch
        {
            ServiceMode.Reforge => "Place an item here to reforge",
            _ => "",
        };
        _costFor = default;
        Refresh(true);
    }

    /// <summary>The panel is put away: the item goes back into the storage and the resident goes about their day.</summary>
    public void Release()
    {
        var npc = Npc;
        Npc = null;
        _pop?.Kill();
        _slot.Scale = Vector2.One;
        StowSlot(_ui);
        _cost.Visible = _hammer.Visible = false;
        if (npc != null && npc.TalkingTo == _ui.Entity) _ui.Game.Town.EndTalk(npc);
    }

    /// <summary>
    /// Puts the service slot's item back into the player's storage (coins and ammo to their slots); what does not fit
    /// drops at the player's feet.
    /// </summary>
    public static void StowSlot(UiContext ui)
    {
        var rest = ui.Inventory.StowService();
        if (!rest.IsEmpty) ui.Game.Drops.Spawn(rest, ui.Entity.Center, System.Numerics.Vector3.Zero);
    }

    public override void _Process(double delta)
    {
        if (Npc is not { } npc || !IsVisibleInTree()) return;
        var town = _ui.Game.Town;
        if (!npc.Alive || !_ui.Entity.Alive || !town.Residents.Contains(npc)
            || System.Numerics.Vector3.Distance(npc.Center, _ui.Entity.Center) > town.Config.TalkRange + 1f)
        {
            CloseRequested?.Invoke();
            return;
        }
        town.HoldTalk(npc, _ui.Entity);
        _costWait -= delta;
        Refresh(false);
    }

    /// <summary>The name on the caption row, the slot with the hammer beside it, and the hint or the cost under them.</summary>
    public void Arrange(float slotScale)
    {
        float side = UiLayout.Slot * slotScale, top = UiLayout.Caption;
        _title.Position = Vector2.Zero;
        _title.Size = new Vector2(Size.X, top);
        _slot.Position = new Vector2(0, top);
        _slot.Resize(side);
        _slot.PivotOffset = _slot.Size / 2;
        _hammer.Position = new Vector2(side + 2 * UiLayout.Gap, top);
        _hammer.Size = new Vector2(side, side);
        _hammer.PivotOffset = _hammer.Size / 2;
        var line = new Vector2(0, top + side + UiLayout.Gap);
        var lineSize = new Vector2(Size.X, MathF.Max(UiLayout.TextLine, Size.Y - line.Y));
        _hint.Position = _cost.Position = line;
        _hint.Size = _cost.Size = lineSize;
    }

    /// <summary>
    /// Presses the hammer (the panel's button and the <c>ui reforge hit</c> command): reforges the slot's item, pops the
    /// slot and shows the new prefix in the tooltip at once. Null when the panel is not open.
    /// </summary>
    public TradeResult? Hit()
    {
        if (Npc is not { } npc) return null;
        var result = _ui.Game.Services.Reforge(npc, _ui.Entity, _ui.Inventory.Service, 0, out _);
        if (result == TradeResult.Ok)
        {
            Reforges++;
            Pop();
            // The follower would notice the new stack next frame; reading it now shows the prefix on this one.
            if (_slot.Hovered) _slot.ShowTooltip();
            else if (_hammer.IsHovered()) ShowTooltip(_hammer);
        }
        else if (result == TradeResult.TooPoor) _ui.Notify("You don't have enough money.", UiTheme.Muted);
        Refresh(true);
        return result;
    }

    /// <summary>
    /// The hammer and the cost while the slot holds something the resident can reforge, else the hint. The cost is
    /// worked out again when the stack changes, and once a second for the resident's mood.
    /// </summary>
    private void Refresh(bool force)
    {
        var stack = _ui.Inventory.Service[0];
        if (!force && stack.Equals(_costFor) && _costWait > 0) return;
        _costFor = stack;
        _costWait = CostRefreshSeconds;
        long cost = Npc != null && Mode == ServiceMode.Reforge ? _ui.Game.Services.ReforgeCost(Npc, _ui.Entity, stack) : 0;
        bool filled = cost > 0;
        _cost.Copper = cost;
        _cost.Visible = _hammer.Visible = filled;
        _hint.Visible = stack.IsEmpty;
    }

    /// <summary>The hammer's hover shows the item it works on, so each press's new prefix is read where the pointer is.</summary>
    private void ShowTooltip(Control owner)
    {
        if (!_ui.Inventory.Service[0].IsEmpty) TooltipFollower.Current?.Show(owner, () => _ui.Inventory.Service[0], _ui.Tooltip);
    }

    /// <summary>The slot pops: 1.1× back to its size about its centre.</summary>
    private void Pop()
    {
        _pop?.Kill();
        _slot.Scale = new Vector2(PopScale, PopScale);
        _pop = _slot.CreateTween();
        _pop.TweenProperty(_slot, "scale", Vector2.One, PopSeconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Terraria's hover: the hammer grows about its centre.</summary>
    private static void Grow(Control c, float scale) => c.CreateTween().TweenProperty(c, "scale", new Vector2(scale, scale), HoverSeconds);
}
