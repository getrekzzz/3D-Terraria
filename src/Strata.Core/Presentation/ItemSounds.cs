using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;

namespace Strata.Core.Presentation;

/// <summary>
/// The sound an item's use makes (P3-27): the item's own <see cref="ItemDef.SoundSet"/> when that sound exists, else
/// its use style's default from data/config/use_styles.json (<see cref="UseStyleEntry.Sound"/>, or
/// <see cref="UseStyleEntry.HeavySound"/> for a heavy item); a drink that restores only mana sounds like mana.
/// Null when neither names an existing sound, so the engine keeps its fallback by ammo and damage class.
/// </summary>
public static class ItemSounds
{
    /// <summary>A use at or above this many ticks makes a gun heavy (the Boomstick, the Musket).</summary>
    public const int HeavyUseTime = 30;

    /// <summary>A spell costing at least this much mana is heavy.</summary>
    public const int HeavyManaCost = 12;

    public static string? Resolve(ItemUseInfo info, UseStyleConfig styles, Func<string, bool> exists)
    {
        var item = info.Item;
        if (item.SoundSet is { } own && exists(own)) return own;
        if (info.Style == UseStyle.Drink && item.HealMana > 0 && item.HealLife <= 0 && exists("mana")) return "mana";
        var style = styles.Style(info.Style);
        if (IsHeavy(item) && style.HeavySound is { } heavy && exists(heavy)) return heavy;
        return style.Sound is { } sound && exists(sound) ? sound : null;
    }

    /// <summary>Slow guns, rocket launchers and costly spells sound heavier.</summary>
    public static bool IsHeavy(ItemDef item) =>
        item.UseAmmo == "rocket" || item.UseAmmo != null && item.UseTime >= HeavyUseTime || item.ManaCost >= HeavyManaCost;
}
