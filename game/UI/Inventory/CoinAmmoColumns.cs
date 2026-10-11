using System;
using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Right of the inventory grid, as in Terraria: four coin slots and four ammo slots in two columns under their
/// captions, the trash slot under the ammo column (its item is saved with the player) and beside the columns the Sort
/// Inventory and Quick Stack to Nearby Chests buttons. Three widgets on the layout's CoinAmmo, Trash and SortButtons
/// regions, each registered for the overlap check; the slots are sized by the layout's slot scale.
/// </summary>
public sealed class CoinAmmoColumns
{
    private const int MinCaptionFont = 9;

    private readonly ItemSlotView[] _coins = new ItemSlotView[PlayerInventory.CoinSlots];
    private readonly ItemSlotView[] _ammo = new ItemSlotView[PlayerInventory.AmmoSlots];
    private readonly OutlinedText _coinCaption, _ammoCaption;
    private readonly ItemSlotView _trash;
    private readonly TextureButton _sort, _quickStack;

    /// <param name="slot">Makes the view of a slot (the screen routes its clicks).</param>
    public CoinAmmoColumns(Func<SlotArea, int, ItemSlotView> slot, Action sort, Action quickStack)
    {
        Columns = new Control { Name = "CoinAmmo", MouseFilter = Control.MouseFilterEnum.Stop };
        Columns.AddChild(_coinCaption = new OutlinedText { Text = "Coins" });
        Columns.AddChild(_ammoCaption = new OutlinedText { Text = "Ammo" });
        for (int i = 0; i < _coins.Length; i++) Columns.AddChild(_coins[i] = slot(SlotArea.Coins, i));
        for (int i = 0; i < _ammo.Length; i++) Columns.AddChild(_ammo[i] = slot(SlotArea.Ammo, i));
        Trash = new Control { Name = "Trash", MouseFilter = Control.MouseFilterEnum.Stop };
        Trash.AddChild(_trash = slot(SlotArea.Trash, 0));
        Buttons = new Control { Name = "SortButtons", MouseFilter = Control.MouseFilterEnum.Stop };
        Buttons.AddChild(_sort = Button("sort", "Sort Inventory", sort));
        Buttons.AddChild(_quickStack = Button("quick_stack", "Quick Stack to Nearby Chests", quickStack));
    }

    /// <summary>The coin and ammo columns with their captions (<see cref="UiRegion.CoinAmmo"/>).</summary>
    public Control Columns { get; }

    /// <summary>The trash slot (<see cref="UiRegion.Trash"/>).</summary>
    public Control Trash { get; }

    /// <summary>The two buttons, one above the other (<see cref="UiRegion.SortButtons"/>).</summary>
    public Control Buttons { get; }

    /// <summary>Moves the three widgets onto their regions; each is hidden when the layout has no room for it.</summary>
    public void Place(UiLayoutResult layout)
    {
        float side = UiLayout.SmallSlot * layout.SlotScale, pitch = side + UiLayout.Gap, top = UiLayout.Caption;
        if (Columns.Visible = LayoutWidgets.Place(Columns, layout, UiRegion.CoinAmmo))
        {
            // Each caption spans its column and half the gap, in the largest font up to Small that fits there.
            float half = side + UiLayout.Gap / 2;
            int font = FittingFont(half);
            Caption(_coinCaption, 0, half, font);
            Caption(_ammoCaption, half, half, font);
            for (int i = 0; i < _coins.Length; i++) Put(_coins[i], 0, top + i * pitch, side);
            for (int i = 0; i < _ammo.Length; i++) Put(_ammo[i], pitch, top + i * pitch, side);
        }
        if (Trash.Visible = LayoutWidgets.Place(Trash, layout, UiRegion.Trash)) Put(_trash, 0, 0, side);
        if (Buttons.Visible = LayoutWidgets.Place(Buttons, layout, UiRegion.SortButtons))
        {
            _sort.Position = Vector2.Zero;
            _quickStack.Position = new Vector2(0, UiLayout.SortButton + UiLayout.Gap);
        }
    }

    private int FittingFont(float width)
    {
        int font = UiFonts.Small;
        while (font > MinCaptionFont && Mathf.Max(TextWidth(_coinCaption.Text, font), TextWidth(_ammoCaption.Text, font)) > width) font--;
        return font;
    }

    private static float TextWidth(string text, int font) =>
        UiFonts.Bold.GetStringSize(text, HorizontalAlignment.Left, -1, font).X + UiFonts.Outline;

    private static void Caption(OutlinedText caption, float x, float width, int font)
    {
        caption.FontSize = font;
        caption.Position = new Vector2(x, 0);
        caption.Size = new Vector2(width, UiLayout.Caption);
    }

    private static void Put(ItemSlotView view, float x, float y, float side)
    {
        view.Position = new Vector2(x, y);
        view.Resize(side);
    }

    /// <summary>A 26 px pixel-art button with Terraria's tooltip text; it never takes keyboard focus (Space jumps).</summary>
    private static TextureButton Button(string sprite, string tooltip, Action pressed)
    {
        var b = new TextureButton
        {
            Name = sprite, TextureNormal = UiSprites.Get(sprite), TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepCentered,
            Size = new Vector2(UiLayout.SortButton, UiLayout.SortButton),
        };
        // Brighter under the mouse, as the reference game's buttons are.
        b.MouseEntered += () => b.SelfModulate = new Color(1.25f, 1.25f, 1.1f);
        b.MouseExited += () => b.SelfModulate = Colors.White;
        b.Pressed += pressed;
        return b;
    }
}
