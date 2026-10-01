using System.Numerics;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Loot;
using Strata.Core.Gameplay.Player;
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
        if (bobber != null && bobber.Biting && HookBoss(p, bobber)) return UseOutcome.Shot;
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

    /// <summary>
    /// Special bait in the backpack whose biome matches the bobber's water hooks its boss instead of a fish: the bait
    /// is used up and the boss surfaces at the bobber.
    /// </summary>
    private bool HookBoss(PlayerEntity p, Entities.Projectile bobber)
    {
        var backpack = p.State.Inventory.Backpack;
        var cell = Physics.VoxelMotion.CellOf(bobber.Position);
        var ctx = _world.Session.ContextAt(cell);
        for (int i = 0; i < backpack.Size; i++)
        {
            if (backpack[i].Item is not { } item || _world.Uses[item].BaitSummon is not { } bait) continue;
            if (bait.Biomes.Length > 0 && !bait.Biomes.Any(ctx.HasBiome)) continue;
            if (!_world.Session.Content.Enemies.TryGet(bait.Enemy, out var boss) || _world.Npcs.All.Any(n => n.Alive && n.Def == boss)) continue;
            backpack.Take(i, 1);
            _world.Projectiles.Reel(bobber); // the line comes back empty: something bigger took the bait
            _world.Npcs.Spawn(boss, bobber.Position + new Vector3(0, 2f, 0));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Throws the held hook along the crosshair, as the grapple key does, or lets go of one already flying or latched
    /// (pressing again releases, as in Terraria). Nothing is thrown from a minecart.
    /// </summary>
    private UseOutcome Grapple(PlayerEntity p, ItemUseInfo info)
    {
        if (info.Grapple is not { } gp || p.Minecart != null) return UseOutcome.None;
        if (p.Hook.State is HookState.Flying or HookState.Attached) p.Hook.Release();
        else p.Hook.Fire(_world.Session.World, gp, info.Item, p.Hand, p.Eye, p.Aim);
        return UseOutcome.Grappled;
    }

    private void Recall(PlayerEntity p)
    {
        p.Hook.Reset();
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
