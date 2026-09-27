using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// Buckets: an empty bucket scoops a cell's worth of the liquid it is aimed at (taken from the pool's surface)
    /// and becomes that liquid's bucket; a filled bucket pours a full cell where it is aimed and becomes empty.
    /// </summary>
    private UseOutcome UseBucket(PlayerEntity p, ItemUseInfo info, float reach)
    {
        var world = _world.Session.World;
        var aim = Targeting.CastLiquid(world, p.Eye, p.Aim, reach);
        if (aim == null) return UseOutcome.OutOfReach;
        var (cell, isLiquid) = aim.Value;
        var editor = _world.Editor;
        var content = _world.Session.Content;
        var cfg = info.Bucket!;

        if (info.BucketLiquid is { } liquid)
        {
            // Pouring onto the same liquid tops it up; otherwise the cell must be open and dry.
            if (isLiquid && world.GetLiquid(cell.X, cell.Y, cell.Z) >> 5 != liquid.Code) return UseOutcome.Blocked;
            if (editor.PourLiquid(cell, liquid, LiquidCode.MaxLevel) == 0) return UseOutcome.Blocked;
            if (cfg.Empty != null && content.Items.TryGet(cfg.Empty, out var empty)) SwapHeld(p, empty);
            return UseOutcome.PouredLiquid;
        }

        if (!isLiquid) return UseOutcome.None;
        var found = content.LiquidOf(world.GetLiquid(cell.X, cell.Y, cell.Z));
        if (cfg.Absorbs != null)
        {
            if (found?.Id != cfg.Absorbs) return UseOutcome.Blocked;
            return editor.DrainLiquid(cell, LiquidCode.MaxLevel, 1).Item2 > 0 ? UseOutcome.CollectedLiquid : UseOutcome.Blocked;
        }
        if (found?.Bucket == null || !content.Items.TryGet(found.Bucket, out var filled)) return UseOutcome.Blocked;
        var (_, taken) = editor.DrainLiquid(cell, LiquidCode.MaxLevel, cfg.MinCollect);
        if (taken == 0) return UseOutcome.Blocked;
        SwapHeld(p, filled);
        return UseOutcome.CollectedLiquid;
    }

    /// <summary>Turns one held item into <paramref name="into"/> (in place for a single item, else stored or dropped).</summary>
    private void SwapHeld(PlayerEntity p, Content.Defs.ItemDef into)
    {
        var inv = p.State.Inventory;
        int slot = inv.SelectedHotbar;
        if (inv.Backpack[slot].Count == 1)
        {
            inv.Backpack[slot] = new ItemStack(into, 1);
            return;
        }
        inv.Backpack.Take(slot, 1);
        var rest = inv.Pickup(new ItemStack(into, 1));
        if (!rest.IsEmpty) _world.Drops.Spawn(rest, p.Center, p.Aim * 2f, pickupDelay: 90);
    }
}
