using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's defence shield beside the equipment column: the shield sprite in a 36 px badge with the defence number
/// centred on it in the 16 px outlined font. Hovering it shows '{n} Defense' and the worn set's bonus in the
/// <see cref="TooltipFollower"/>. The number and the cached tooltip text change only when the stats do.
/// </summary>
public sealed partial class DefenseBadge : Control
{
    private readonly OutlinedText _number;
    private int _defense = -1;
    private string? _setBonus;
    private string _tooltip = "";

    public DefenseBadge()
    {
        Name = "DefenseBadge";
        MouseFilter = MouseFilterEnum.Stop;
        Size = CustomMinimumSize = new Vector2(UiLayout.DefenseBadge, UiLayout.DefenseBadge);
        var shield = new TextureRect
        {
            // At its own pixel size (pixel sprites stay crisp only at integral scales), centred in the 36 px badge.
            Texture = UiSprites.Get("shield"), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shield.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shield);
        _number = new OutlinedText { FontSize = UiFonts.Body };
        _number.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_number);
        MouseEntered += () => TooltipFollower.Current?.ShowText(this, () => _tooltip);
        MouseExited += () => TooltipFollower.Current?.Hide(this);
    }

    /// <summary>The defence shown (the <c>ui.defense_badge</c> probe); -1 before the first update.</summary>
    public int Shown => _defense;

    /// <summary>Follows <paramref name="state"/>'s defence and set bonus (called each frame while the inventory is open).</summary>
    public void Update(PlayerState state)
    {
        if (state.Defense == _defense && state.SetBonus == _setBonus) return;
        _defense = state.Defense;
        _setBonus = state.SetBonus;
        _number.Text = _defense.ToString();
        _tooltip = _setBonus != null ? $"{_defense} Defense\nSet bonus: {_setBonus}" : $"{_defense} Defense";
    }
}
