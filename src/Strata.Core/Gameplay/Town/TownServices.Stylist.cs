using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

public sealed partial class TownServices
{
    // ---- The Stylist's haircut ---------------------------------------------------------------------------------

    /// <summary>
    /// What a haircut to <paramref name="style"/> and <paramref name="colorHex"/> costs now (copper): 0 when nothing
    /// changes, -1 when this resident cuts no hair or the style or colour is not valid.
    /// </summary>
    public long HaircutCost(TownNpc npc, PlayerEntity player, string style, string colorHex)
    {
        if (npc.Def.FindService(TownServiceSpecs.Haircut) is not { } spec) return -1;
        var look = player.State.Look;
        if (Array.IndexOf(Appearance.StylesFor(look.Body), style) < 0 || !HairDyes.TryParseHex(colorHex, out _)) return -1;
        var p = spec.Parse<HaircutParams>();
        long cost = (style != look.HairStyle ? p.Price : 0) + (!string.Equals(colorHex, look.HairColor, StringComparison.OrdinalIgnoreCase) ? p.ColorPrice : 0);
        return cost == 0 ? 0 : Math.Max(1, (long)MathF.Round(cost * _commerce.PriceFactor(npc)));
    }

    /// <summary>Cuts and colours the player's hair for coins; the avatar picks the new look up through <see cref="PlayerState.LookChanged"/>.</summary>
    public TradeResult Haircut(TownNpc npc, PlayerEntity player, string style, string colorHex)
    {
        long cost = HaircutCost(npc, player, style, colorHex);
        if (cost < 0) return TradeResult.Refused;
        if (cost == 0) return TradeResult.NothingToDo;
        var inv = player.State.Inventory;
        if (!Wallet.TrySpend(inv, player.State.BanksForPaying(), Content, cost, out var change)) return TradeResult.TooPoor;
        foreach (var s in change) _world.Drops.Spawn(s, player.Center, System.Numerics.Vector3.Zero);
        var look = player.State.Look;
        look.HairStyle = style;
        look.HairColor = colorHex.ToLowerInvariant();
        player.State.RaiseLookChanged();
        return TradeResult.Ok;
    }
}
