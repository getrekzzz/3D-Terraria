using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

/// <summary>
/// Use time vs use animation, as in Terraria: <see cref="PlayerEntity.UseAnimation"/> (item animation) is the swing or
/// pose, played once per use and never restarted mid-arc; <see cref="PlayerEntity.UseCooldown"/> (item time) is the
/// action cadence. Speed prefixes and melee speed shorten both; mining speed shortens only a tool's use time, so it
/// digs more often inside the same swing.
/// </summary>
public sealed partial class ItemUseSystem
{
    /// <summary>Clip windows (wind-up, strike) shared with the melee system and presentation.</summary>
    public UseTiming Timing { get; set; } = UseTiming.Default;

    /// <summary>
    /// How much faster than listed the held stack is used: its prefix, times melee speed (Feral Claws) for swung and
    /// thrust weapons and tools.
    /// </summary>
    public static float UseSpeed(PlayerEntity p, ItemStack held, ItemUseInfo info)
    {
        float speed = Prefixes.UseSpeed(held);
        if (info.Has(ItemAction.Melee | ItemAction.Thrust)) speed *= 1f + p.State.Effects.Stats.Bonus(StatIds.MeleeSpeed);
        return MathF.Max(0.1f, speed);
    }

    /// <summary>Use time in ticks at <paramref name="speed"/> (at least 1; float noise never adds a tick).</summary>
    private static int UseTicks(float ticks) => Math.Max(1, (int)MathF.Ceiling(ticks - 1e-3f));

    /// <summary>Starts a new use animation: speed-scaled length, a new serial for presentation, the animating item.</summary>
    private static void StartAnimation(PlayerEntity p, ItemDef item, float speed)
    {
        p.UseAnimation = p.UseAnimationMax = Math.Max(1, (int)MathF.Round(item.UseAnimation / speed));
        p.UseSerial++;
        p.UseItem = item;
    }
}
