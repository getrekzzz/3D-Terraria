using Godot;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// Drawing a slot as Terraria does, from cached styles so a redraw allocates nothing: the tinted slot (gold when
/// selected or favourited, dark when locked), the icon at ~62% of the slot (greyed when locked or dimmed), the stack
/// count bottom-left in white with a black outline (red for a material the player is short of), the ammo count of a bow
/// or gun in the same place (red at 0), the badge top-left and the empty-slot silhouette.
/// </summary>
public sealed partial class ItemSlotView
{
    // The reference game's favourite slot: a gold wash behind the icon, under the gold rim.
    private static readonly Color FavoriteTint = new(1f, 0.78f, 0.25f, 0.3f);
    private static readonly Color HintTint = new(1f, 1f, 1f, 0.25f);
    private static readonly Color LockedIcon = new(0.55f, 0.55f, 0.6f, 0.8f);
    private static readonly Color OutOfAmmo = Color.Color8(255, 70, 70);
    private const float IconFraction = 0.62f;

    private SlotKind StyleKind => _locked ? SlotKind.Locked : _selected ? SlotKind.HotbarSelected : _favoriteShown ? SlotKind.Favorite : _kind;

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(UiTheme.SlotStyle(StyleKind, _hovered && !_locked), rect);
        if (_favoriteShown && !_selected && !_locked) DrawRect(rect.Grow(-3), FavoriteTint);
        var icon = new Vector2(Size.X * IconFraction, Size.Y * IconFraction);
        var iconRect = new Rect2((Size - icon) * 0.5f, icon);
        var s = _shown;
        if (!s.IsEmpty)
        {
            DrawTextureRect(ItemIcons.Get(s.Item!), iconRect, false, _locked || _dimmed ? LockedIcon : Colors.White);
            // A weapon never stacks, so the rounds it has left take the stack count's corner.
            if (_ammoShown >= 0) Count(_ammoShown.ToString(), _ammoShown == 0 ? OutOfAmmo : UiTheme.Text);
            else if (s.Count > 1 || _short) Count(s.Count.ToString(), _short ? OutOfAmmo : UiTheme.Text);
        }
        else if (EmptyHint != null)
        {
            // Pixel-art silhouettes keep their own size, so they stay crisp.
            var hint = EmptyHint.GetSize();
            DrawTextureRect(EmptyHint, new Rect2(((Size - hint) * 0.5f).Round(), hint), false, HintTint);
        }
        if (Badge != null) Outlined(new Vector2(Size.X * 0.1f, UiFonts.Small + 2), Badge, UiFonts.Small, UiTheme.Text);
    }

    /// <summary>The stack (or ammo) count at the bottom-left: a bold white number with a 4 px black outline.</summary>
    private void Count(string text, Color color) =>
        Outlined(new Vector2(Size.X * 0.12f, Size.Y - 6), text, Size.X >= 56 ? UiFonts.Body : UiFonts.Stack, color);

    private void Outlined(Vector2 baseline, string text, int size, Color color)
    {
        var font = UiFonts.Bold;
        DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, size, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, color);
    }
}
