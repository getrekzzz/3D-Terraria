using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The hotbar in the top-left corner, as in Terraria: ten slots with their number keys, the selected one gold and full
/// size while the others ease down to <see cref="Unselected"/> of it about their centres; bows and guns show the rounds
/// carried. Clicking a slot selects it, and every switch (keys, wheel, click) ticks. The HUD hides it while the
/// inventory is open, whose grid takes its place.
/// </summary>
public sealed partial class HotbarBar : Control
{
    /// <summary>The scale of the slots that are not selected.</summary>
    public const float Unselected = 0.78f;

    private const float EaseRate = 3f; // scale per second

    private readonly UiContext _ui;
    private readonly ItemSlotView[] _slots = new ItemSlotView[PlayerInventory.HotbarSize];
    private int _selected = -1;

    public HotbarBar(UiContext ui)
    {
        _ui = ui;
        Name = "Hotbar";
        MouseFilter = MouseFilterEnum.Ignore;
        for (int i = 0; i < _slots.Length; i++)
        {
            int slot = i;
            var view = new ItemSlotView(() => ui.Inventory.Backpack[slot], UiLayout.Slot, SlotKind.Inventory, ui.Tooltip)
            {
                Badge = ((i + 1) % 10).ToString(),
                Favorite = () => ui.Inventory.Backpack.IsFavorite(slot),
                AmmoCount = ammoClass => ui.Inventory.CountAmmo(ammoClass),
                Scale = new Vector2(Unselected, Unselected),
            };
            view.Clicked += (_, _) => ui.Inventory.Select(slot);
            AddChild(view);
            _slots[i] = view;
        }
        Resized += Arrange;
    }

    /// <summary>The slot view at <paramref name="index"/>, or null.</summary>
    public ItemSlotView? Slot(int index) => (uint)index < (uint)_slots.Length ? _slots[index] : null;

    public override void _Process(double delta)
    {
        int selected = _ui.Inventory.SelectedHotbar;
        if (selected != _selected)
        {
            if (_selected >= 0) Audio.Sfx.Play("hotbar_tick", pitchJitter: 0.03f);
            _selected = selected;
            for (int i = 0; i < _slots.Length; i++) _slots[i].Selected = i == selected;
        }
        float step = EaseRate * (float)delta;
        for (int i = 0; i < _slots.Length; i++)
        {
            var view = _slots[i];
            float target = i == selected ? 1f : Unselected;
            float scale = view.Scale.X;
            if (scale == target) continue;
            scale = Mathf.MoveToward(scale, target, step);
            view.Scale = new Vector2(scale, scale);
        }
    }

    /// <summary>Ten square slots across the bar's width, each scaling about its centre.</summary>
    private void Arrange()
    {
        float side = Mathf.Min(Size.Y, (Size.X - (_slots.Length - 1) * UiLayout.Gap) / _slots.Length);
        for (int i = 0; i < _slots.Length; i++)
        {
            var view = _slots[i];
            view.Position = new Vector2(i * (side + UiLayout.Gap), 0);
            view.CustomMinimumSize = view.Size = new Vector2(side, side);
            view.PivotOffset = view.Size / 2;
        }
    }
}
