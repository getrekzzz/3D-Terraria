using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// One inventory slot: background, item icon, stack count, selection/hover state and a rich tooltip.
/// Reads its stack through a delegate so it always shows the live inventory.
/// </summary>
public sealed partial class ItemSlotView : Control
{
    private readonly Func<ItemStack> _read;
    private readonly Func<ItemStack, System.Collections.Generic.List<(string Text, TooltipKind Kind)>>? _tooltip;
    private bool _hovered;
    private ItemStack _shown;

    public ItemSlotView(Func<ItemStack> read, float size, Func<ItemStack, System.Collections.Generic.List<(string, TooltipKind)>>? tooltip = null)
    {
        _read = read;
        _tooltip = tooltip;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hovered = true; QueueRedraw(); };
        MouseExited += () => { _hovered = false; QueueRedraw(); };
    }

    public bool Selected { get; set; }

    /// <summary>Short label drawn in the corner (hotbar numbers).</summary>
    public string? Badge { get; set; }

    /// <summary>Icon shown dimmed when empty (armor slot silhouettes).</summary>
    public Texture2D? EmptyHint { get; set; }

    /// <summary>(button, shift) on click.</summary>
    public event Action<MouseButton, bool>? Clicked;

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.slot_views");
        var s = _read();
        if (!s.Equals(_shown))
        {
            _shown = s;
            TooltipText = s.IsEmpty ? "" : s.Item!.DisplayName;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
        {
            Clicked?.Invoke(mb.ButtonIndex, mb.ShiftPressed);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(UiTheme.Slot(Selected, _hovered), rect);
        var s = _shown;
        var inner = rect.Grow(-Size.X * 0.14f);
        if (!s.IsEmpty)
        {
            DrawTextureRect(ItemIcons.Get(s.Item!), inner, false);
            if (s.Count > 1)
            {
                var font = GetThemeDefaultFont();
                int fs = Mathf.RoundToInt(Size.X * 0.27f);
                var text = s.Count.ToString();
                var pos = new Vector2(Size.X * 0.14f, Size.Y - Size.Y * 0.1f);
                DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fs, 4, new Color(0, 0, 0, 0.9f));
                DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, UiTheme.Text);
            }
        }
        else if (EmptyHint != null)
        {
            DrawTextureRect(EmptyHint, inner, false, new Color(1, 1, 1, 0.2f));
        }
        if (Badge != null)
        {
            var font = GetThemeDefaultFont();
            DrawString(font, new Vector2(4, 13), Badge, HorizontalAlignment.Left, -1, 11, UiTheme.Muted);
        }
    }

    public override Control? _MakeCustomTooltip(string forText)
    {
        var s = _read();
        if (s.IsEmpty || _tooltip == null) return null;
        var box = new VBoxContainer();
        foreach (var (text, kind) in _tooltip(s))
        {
            var label = new Label { Text = text };
            label.AddThemeColorOverride("font_color", kind switch
            {
                TooltipKind.Name => UiTheme.Rarity(s.Item!.Rarity),
                TooltipKind.Flavor => new Color(0.75f, 0.8f, 0.95f),
                TooltipKind.Price => new Color(0.95f, 0.85f, 0.5f),
                TooltipKind.Info => UiTheme.Muted,
                _ => UiTheme.Text,
            });
            box.AddChild(label);
        }
        return box;
    }
}
