using System;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Presentation;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>What an icon on the buff bar stands for.</summary>
public enum BuffIconKind
{
    Buff,

    /// <summary>The summons of one minion kind (Terraria's minion buff).</summary>
    Minion,

    /// <summary>Riding a minecart.</summary>
    Mount,
}

/// <summary>
/// One icon on the buff bar (32 × 46): the buff's 32 px icon with its countdown under it ("4 m", "30 s"; none where
/// <see cref="BuffDisplay.ShowsTimer"/> says so), blinking through its last 5 s; a minion kind as its staff's icon with
/// how many are summoned; the minecart while riding. While the pointer is free (a screen is open) hovering shows the
/// name and effect beside the pointer and a right click raises <see cref="Cancelled"/>; during play it takes no clicks.
/// </summary>
public sealed partial class BuffIconView : Control
{
    private const int BlinkTicks = 5 * BuffDisplay.TicksPerSecond;
    private const int TimerFont = 12;

    private readonly ContentDatabase _content;
    private Texture2D? _icon;
    private ItemDef? _item;
    private string _timer = "", _count = "";
    private int _seconds = -1;
    private double _blink;

    public BuffIconView(ContentDatabase content)
    {
        _content = content;
        MouseFilter = MouseFilterEnum.Ignore;
        Size = CustomMinimumSize = new Vector2(UiLayout.BuffIcon, UiLayout.BuffCellHeight);
        MouseEntered += ShowTooltip;
        MouseExited += () => TooltipFollower.Current?.Hide(this);
    }

    public BuffIconKind Kind { get; private set; }

    /// <summary>The buff shown (<see cref="BuffIconKind.Buff"/>).</summary>
    public ActiveBuff? Buff { get; private set; }

    /// <summary>The minion kind shown (<see cref="BuffIconKind.Minion"/>).</summary>
    public ProjectileDef? Minion { get; private set; }

    /// <summary>Minions of that kind summoned.</summary>
    public int Count { get; private set; }

    /// <summary>The name <c>ui buff cancel</c> takes: the buff's id, the minion's projectile id, or "minecart".</summary>
    public string Id => Kind switch
    {
        BuffIconKind.Buff => Buff!.Def.Id,
        BuffIconKind.Minion => Minion!.Id,
        _ => "minecart",
    };

    /// <summary>A buff shown with its generated icon rather than the letter square standing in for a missing one.</summary>
    public bool HasBuffIcon { get; private set; }

    /// <summary>Right-clicked: the bar cancels the buff or sends the minions home.</summary>
    public event Action<BuffIconView>? Cancelled;

    public void BindBuff(ActiveBuff buff)
    {
        if (Kind == BuffIconKind.Buff && Buff == buff) return;
        Rebound(BuffIconKind.Buff);
        Buff = buff;
        _icon = StatusIcons.Get(buff.Def);
        HasBuffIcon = !StatusIcons.IsFallback(_icon);
        TextureFilter = TextureFilterEnum.Nearest; // 32 px pixel art
    }

    /// <summary>A minion kind, drawn as the staff that summons it (<paramref name="staff"/>) with the count.</summary>
    public void BindMinion(ProjectileDef minion, ItemDef? staff, int count)
    {
        if (Kind != BuffIconKind.Minion || Minion != minion) Rebound(BuffIconKind.Minion);
        else if (Count == count) return;
        Minion = minion;
        Count = count;
        _count = count.ToString();
        BindItem(staff);
    }

    public void BindMount(ItemDef? cart)
    {
        if (Kind == BuffIconKind.Mount) return;
        Rebound(BuffIconKind.Mount);
        BindItem(cart);
    }

    /// <summary>Per frame while shown: the countdown when its whole seconds change, and the blink near the end.</summary>
    public void Tick(double delta)
    {
        if (Kind != BuffIconKind.Buff || !BuffDisplay.ShowsTimer(Buff!.Def)) return;
        int ticks = Buff.TicksLeft;
        int seconds = (ticks + BuffDisplay.TicksPerSecond - 1) / BuffDisplay.TicksPerSecond;
        if (seconds != _seconds)
        {
            _seconds = seconds;
            string text = BuffDisplay.Timer(ticks);
            if (text != _timer)
            {
                _timer = text;
                QueueRedraw();
            }
        }
        if (ticks > BlinkTicks)
        {
            if (Modulate.A < 1) Modulate = Colors.White;
            return;
        }
        _blink += delta;
        Modulate = new Color(1, 1, 1, 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Cos((float)_blink * Mathf.Tau * 1.5f)));
    }

    public override void _Draw()
    {
        if (_icon != null) DrawTextureRect(_icon, new Rect2(0, 0, UiLayout.BuffIcon, UiLayout.BuffIcon), false);
        var font = UiFonts.Bold;
        if (_count.Length > 0)
        {
            // Bottom-right on the icon, like a stack count.
            var at = new Vector2(0, UiLayout.BuffIcon - 2);
            DrawStringOutline(font, at, _count, HorizontalAlignment.Right, UiLayout.BuffIcon - 1, UiFonts.Small, UiFonts.Outline, UiFonts.OutlineColor);
            DrawString(font, at, _count, HorizontalAlignment.Right, UiLayout.BuffIcon - 1, UiFonts.Small, UiTheme.Text);
        }
        if (_timer.Length == 0) return;
        // Centred under the icon, across the gap to the next one.
        float overhang = (UiLayout.BuffPitchX - UiLayout.BuffIcon) / 2;
        var baseline = new Vector2(-overhang, UiLayout.BuffIcon + font.GetAscent(TimerFont) + 1);
        DrawStringOutline(font, baseline, _timer, HorizontalAlignment.Center, UiLayout.BuffPitchX, TimerFont, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, baseline, _timer, HorizontalAlignment.Center, UiLayout.BuffPitchX, TimerFont, UiTheme.Text);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb || (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right)) return;
        AcceptEvent(); // a click on the bar never reaches the world (the wheel still switches the hotbar)
        if (mb.ButtonIndex == MouseButton.Right) Cancelled?.Invoke(this);
    }

    /// <summary>Shows the name and effect (on hover, and from the bar).</summary>
    public void ShowTooltip()
    {
        var tooltip = TooltipFollower.Current;
        if (tooltip == null) return;
        switch (Kind)
        {
            case BuffIconKind.Buff:
                tooltip.ShowText(this, Buff!.Def.DisplayName, BuffText.Describe(Buff.Def, _content));
                break;
            case BuffIconKind.Minion:
                string name = Minion!.DisplayName;
                tooltip.ShowText(this, name, $"The {name.ToLowerInvariant()} will fight for you" + (Count > 1 ? $"\n{Count} summoned" : ""));
                break;
            default:
                tooltip.ShowText(this, _item?.DisplayName ?? "Minecart", "Riding in a minecart");
                break;
        }
    }

    /// <summary>Now stands for something else: no countdown, blink or tooltip of the old one.</summary>
    private void Rebound(BuffIconKind kind)
    {
        Kind = kind;
        Buff = null;
        Minion = null;
        Count = 0;
        _item = null;
        _count = _timer = "";
        _seconds = -1;
        _blink = 0;
        HasBuffIcon = false;
        Modulate = Colors.White;
        TooltipFollower.Current?.Hide(this);
        QueueRedraw();
    }

    private void BindItem(ItemDef? item)
    {
        _item = item;
        _icon = item != null ? ItemIcons.Get(item) : null;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps; // item icons are drawn at a quarter of their size
        QueueRedraw();
    }
}
