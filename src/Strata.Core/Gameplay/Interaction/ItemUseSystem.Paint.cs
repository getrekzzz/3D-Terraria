using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>Player placing blocks right now (the Paint Sprayer paints what they place).</summary>
    private PlayerEntity? _placer;

    /// <summary>Paintbrush: paints the aimed block with the backpack's first paint (one per block). Paint Scraper: scrapes it off.</summary>
    private UseOutcome UsePaint(PlayerEntity p, PaintToolParams cfg, Target target)
    {
        if (cfg.Scrape) return _world.Editor.PaintBlock(target.Cell, 0) ? UseOutcome.Painted : UseOutcome.Blocked;
        int slot = PaintSlot(p);
        if (slot < 0) return UseOutcome.NoAmmo;
        var paint = p.State.Inventory.Backpack[slot].Item!;
        if (!_world.Editor.PaintBlock(target.Cell, paint.Paint)) return UseOutcome.Blocked;
        p.State.Inventory.Backpack.Take(slot, 1);
        return UseOutcome.Painted;
    }

    /// <summary>With a Paint Sprayer worn, a block the player just placed is painted too (using up paint).</summary>
    private void SprayPlaced(Int3 cell)
    {
        var p = _placer;
        if (p == null || !p.State.Effects.AutoPaint) return;
        int slot = PaintSlot(p);
        if (slot < 0) return;
        if (_world.Editor.PaintBlock(cell, p.State.Inventory.Backpack[slot].Item!.Paint)) p.State.Inventory.Backpack.Take(slot, 1);
    }

    private static int PaintSlot(PlayerEntity p)
    {
        var backpack = p.State.Inventory.Backpack;
        for (int i = 0; i < backpack.Size; i++)
            if (backpack[i].Item is { Paint: > 0 }) return i;
        return -1;
    }
}
