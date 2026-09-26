using System.Numerics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    private LootRoller? _loot;
    private Determinism.Pcg32 _openRng = new(0x0B3E7);

    /// <summary>A player was sent home (Magic Mirror, Recall Potion): the game moves the body to the new feet position.</summary>
    public event Action<PlayerEntity, Vector3>? Recalled;

    /// <summary>A catch was reeled in (item, count) — for the pickup message.</summary>
    public event Action<PlayerEntity, ItemStack>? Caught;

    /// <summary>
    /// Fishing rods: the first use casts the bobber; the next reels it in, landing whatever is biting. The catch goes
    /// to the backpack (or drops at the player's feet when it is full).
    /// </summary>
    private UseOutcome Fish(PlayerEntity p, ItemUseInfo info)
    {
        var bobber = _world.Projectiles.BobberOf(p);
        if (bobber != null)
        {
            foreach (var stack in _world.Projectiles.Reel(bobber))
            {
                Caught?.Invoke(p, stack);
                Give(p, stack);
            }
            return UseOutcome.Shot;
        }
        if (info.Projectile == null) return UseOutcome.None;
        Shot?.Invoke(new ShotRequest(p, info, info.Projectile, null, info.Item.ShootSpeed, 0, 0));
        return UseOutcome.Shot;
    }

    private void Recall(PlayerEntity p)
    {
        var home = _world.SpawnPositionFor(p.State);
        p.Position = home;
        p.Velocity = Vector3.Zero;
        Recalled?.Invoke(p, home);
    }

    /// <summary>Opens a crate or bag into the backpack.</summary>
    private void Open(PlayerEntity p, string table)
    {
        _loot ??= new LootRoller(_world.Session.Content);
        var ctx = _world.Session.ContextAt(p.FeetCell);
        foreach (var stack in _loot.Roll(table, ref _openRng, ctx)) Give(p, stack);
    }

    private void Give(PlayerEntity p, ItemStack stack)
    {
        var rest = p.State.Inventory.Pickup(stack);
        if (!rest.IsEmpty) _world.Drops.Spawn(rest, p.Center, Vector3.Zero);
    }
}
