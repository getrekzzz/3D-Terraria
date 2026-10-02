namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>
    /// A player regained life (false) or mana (true) from something they did or bought: a potion, a crystal, life
    /// steal, the nurse. Natural regeneration does not report. The game floats a green or blue number for it.
    /// </summary>
    public event Action<PlayerEntity, int, bool>? PlayerHealed;

    /// <summary>Announces a heal of <paramref name="amount"/> life (or mana); nothing when the amount is 0.</summary>
    public void ReportHeal(PlayerEntity p, int amount, bool mana)
    {
        if (amount > 0) PlayerHealed?.Invoke(p, amount, mana);
    }
}
