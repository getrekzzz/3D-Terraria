using Strata.Core.Content;
using Strata.Core.Gameplay.Player;

namespace Strata.Core.Gameplay.Items;

/// <summary>
/// What a tooltip may know beyond the stack itself: whether the item is a crafting material, the content (buff text,
/// station names), the viewing player (class bonuses, worn set) and the price to show (a shop's buy price, or the
/// town's sell ratio). Without content or a player the tooltip shows the item's own, unadjusted stats.
/// </summary>
public sealed record TooltipContext(bool UsedInRecipes, ContentDatabase? Content, PlayerState? Player, float SellRatio = 0.2f, long? BuyPrice = null)
{
    /// <summary>A context with nothing but the material flag (tests, previews).</summary>
    public static TooltipContext Plain(bool usedInRecipes) => new(usedInRecipes, null, null);
}
