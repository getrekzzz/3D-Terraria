using System.Numerics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>A hardcore character died: its save is gone and the game should leave the world.</summary>
    public event Action<PlayerEntity>? PlayerLost;

    /// <summary>
    /// What death costs by character difficulty, as in the reference game: Classic drops half the coins carried,
    /// Mediumcore drops the whole inventory, Hardcore drops everything and deletes the character.
    /// </summary>
    private void ApplyDeathPenalty(PlayerEntity p)
    {
        var state = p.State;
        var inv = state.Inventory;
        var at = p.Center;
        switch (state.Difficulty)
        {
            case CharacterDifficulty.Classic:
                long coins = Coins.Total(inv.Backpack);
                if (coins < 2) return;
                if (Wallet.TrySpend(inv.Backpack, Session.Content, coins / 2, out var change))
                    foreach (var s in change) Drops.Spawn(s, at, Scatter(), pickupDelay: 60);
                foreach (var s in Coins.ToStacks(Session.Content, coins / 2)) Drops.Spawn(s, at, Scatter(), pickupDelay: 60);
                break;
            case CharacterDifficulty.Mediumcore:
            case CharacterDifficulty.Hardcore:
                foreach (var container in new[] { inv.Backpack, inv.Armor, inv.Accessories })
                    for (int i = 0; i < container.Size; i++)
                    {
                        if (container[i].IsEmpty) continue;
                        Drops.Spawn(container[i], at, Scatter(), pickupDelay: 60);
                        container[i] = ItemStack.Empty;
                    }
                state.Recompute();
                if (state.Difficulty == CharacterDifficulty.Hardcore)
                {
                    p.Lost = true;
                    if (PlayerDirectory != null) DeletePlayer(PlayerDirectory, state.Name);
                    PlayerLost?.Invoke(p);
                }
                break;
        }
    }

    private Vector3 Scatter()
    {
        float a = (_deathScatter++ * 2.39996f) % MathF.Tau; // golden-angle spread, deterministic
        return new Vector3(MathF.Cos(a) * 2.5f, 3.5f, MathF.Sin(a) * 2.5f);
    }

    private int _deathScatter;
}
