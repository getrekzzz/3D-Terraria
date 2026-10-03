using Strata.Core.Session;

namespace Strata.Game.Player;

/// <summary>
/// Whether a player's weapon is out of their hand: a yoyo or flail on its tether (<see cref="Strata.Core.Gameplay.Entities.ProjectileSystem.TetherOf"/>,
/// out until it is back home) or a whip's lash mid-crack. Both views hide the held model meanwhile, so the weapon is
/// not drawn twice (in the fist and on the string); it shows again once the tether or lash is gone.
/// </summary>
public static class HeldTether
{
    public static bool Out(GameWorld game, PlayerEntity p)
    {
        if (game.Projectiles.TetherOf(p) != null) return true;
        foreach (var lash in game.Whips.Active)
            if (lash.Owner == p) return true;
        return false;
    }
}
