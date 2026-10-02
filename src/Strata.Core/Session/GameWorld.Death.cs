using System.Numerics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>A hardcore character died: its save is gone and the game should leave the world.</summary>
    public event Action<PlayerEntity>? PlayerLost;

    /// <summary>A player died, with the death message naming the cause ("Wanderer was slain by a Zombie.").</summary>
    public event Action<PlayerEntity, string>? PlayerSlain;

    /// <summary>The death message lines (data/config/deaths.json; loaded by the launcher). Without them every death "was slain".</summary>
    public Presentation.DeathMessages Deaths { get; set; } = new();

    /// <summary>The message for <paramref name="p"/>'s latest death, from what hurt them last; the line follows the death count.</summary>
    public string DeathMessage(PlayerEntity p) => Deaths.Format(p.State.Name, p.LastHarm, p.State.Deaths - 1);

    /// <summary>
    /// What death costs by character difficulty, as in the reference game: Classic drops half the coins carried (coin
    /// slots and backpack), Mediumcore drops the whole inventory, Hardcore drops everything and deletes the character.
    /// </summary>
    private void ApplyDeathPenalty(PlayerEntity p)
    {
        var state = p.State;
        var inv = state.Inventory;
        var at = p.Center;
        switch (state.Difficulty)
        {
            case CharacterDifficulty.Classic:
                long coins = Coins.Total(inv);
                if (coins < 2) return;
                if (Wallet.TrySpend(inv, Session.Content, coins / 2, out var change))
                    foreach (var s in change) Drops.Spawn(s, at, Scatter(), pickupDelay: 60);
                foreach (var s in Coins.ToStacks(Session.Content, coins / 2)) Drops.Spawn(s, at, Scatter(), pickupDelay: 60);
                break;
            case CharacterDifficulty.Mediumcore:
            case CharacterDifficulty.Hardcore:
                // Backpack, coins, ammo and everything worn (vanity, dyes, pet, mount, hook…); the stats follow the gear.
                foreach (var container in inv.AllStorages)
                    for (int i = 0; i < container.Size; i++)
                    {
                        if (container[i].IsEmpty) continue;
                        Drops.Spawn(container[i], at, Scatter(), pickupDelay: 60);
                        container[i] = ItemStack.Empty;
                    }
                if (state.Difficulty == CharacterDifficulty.Hardcore)
                {
                    p.Lost = true;
                    if (PlayerDirectory != null) DeleteLostPlayer(PlayerDirectory, state.Name);
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
