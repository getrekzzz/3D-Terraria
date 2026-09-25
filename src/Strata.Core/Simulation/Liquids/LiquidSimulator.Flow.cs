using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;

namespace Strata.Core.Simulation.Liquids;

public sealed partial class LiquidSimulator
{
    /// <summary>Evaluates one active cell: react, fall, spread, or evaporate.</summary>
    private void FlowCell(EditBatch batch, LiquidDef def, Int3 cell)
    {
        var cells = new BatchCells(batch);
        byte current = batch.GetLiquid(cell);
        if (current == 0 || current >> 5 != def.Code) return;
        ushort block = batch.GetBlock(cell);
        if (block == VoxelWorld.Unknown) return; // unloaded: the chunk-load scan wakes it again
        if (_rules.IsSolid(block))
        {
            // A block was placed into the liquid: it is displaced.
            Write(batch, cell, 0);
            return;
        }
        int level = current & 0x1F;
        if (React(batch, def, cell, ref level)) return;
        if (Fall(ref cells, batch, def, cell, ref level)) return;
        if (Spread(ref cells, batch, def, cell, level)) return;
        if (level < def.MinLevel && def.EvaporateChance > 0)
        {
            if (Roll(cell) < def.EvaporateChance) Write(batch, cell, 0);
            else _keep.Add(cell);
        }
    }

    /// <summary>
    /// Meeting a liquid it reacts with hardens a cell into the mix block: the neighbour's cell when this liquid
    /// arrives from above or beside, this cell when the other liquid pours onto it. Returns true when this
    /// cell is used up.
    /// </summary>
    private bool React(EditBatch batch, LiquidDef def, Int3 cell, ref int level)
    {
        foreach (var o in Faces.Offsets)
        {
            var n = cell + o;
            byte other = batch.GetLiquid(n);
            if (other == 0 || other >> 5 == def.Code) continue;
            ushort mix = _content.MixOf(def.Code, other >> 5);
            if (mix == 0) continue;
            var target = o.Y > 0 ? cell : n;
            batch.SetLiquid(target, 0);
            batch.SetBlock(target, mix);
            _changed.Add(target);
            Mixed?.Invoke(target, _content.Blocks[mix]);
            if (target == cell) return true;
            level = Math.Max(0, level - ReactionCost);
            Write(batch, cell, LiquidCode.Pack((LiquidType)def.Code, level));
            if (level == 0) return true;
        }
        return false;
    }

    /// <summary>Pours down the column (up to <see cref="LiquidDef.FallCells"/>) into air or unfilled same liquid.</summary>
    private bool Fall(ref BatchCells cells, EditBatch batch, LiquidDef def, Int3 cell, ref int level)
    {
        var dst = cell;
        int have = 0;
        for (int k = 1; k <= def.FallCells; k++)
        {
            var c = new Int3(cell.X, cell.Y - k, cell.Z);
            if (!_rules.Open(ref cells, c)) break;
            byte l = batch.GetLiquid(c);
            if (l == 0)
            {
                dst = c;
                have = 0;
                continue;
            }
            if (l >> 5 == def.Code && (l & 0x1F) < LiquidCode.MaxLevel)
            {
                dst = c;
                have = l & 0x1F;
            }
            break;
        }
        if (dst == cell) return false;
        int amount = Math.Min(level, LiquidCode.MaxLevel - have);
        var type = (LiquidType)def.Code;
        Write(batch, dst, LiquidCode.Pack(type, have + amount));
        _received.Add(dst);
        level -= amount;
        Write(batch, cell, LiquidCode.Pack(type, level));
        return true;
    }

    /// <summary>
    /// Shares sideways with neighbours at least <see cref="LiquidDef.SpreadThreshold"/> levels lower, evening the
    /// group out. Too little to cover them all at <see cref="LiquidDef.MinLevel"/> pours toward the lowest one
    /// only, and too little for that leaves a settled puddle.
    /// </summary>
    private bool Spread(ref BatchCells cells, EditBatch batch, LiquidDef def, Int3 cell, int level)
    {
        Span<int> levels = stackalloc int[4];
        Span<int> which = stackalloc int[4];
        int count = 0, total = level;
        for (int i = 0; i < LiquidRules.Horizontal.Length; i++)
        {
            var n = cell + LiquidRules.Horizontal[i];
            if (!_rules.Open(ref cells, n)) continue;
            byte l = batch.GetLiquid(n);
            if (l != 0 && l >> 5 != def.Code) continue;
            int nl = l & 0x1F;
            if (level - nl < def.SpreadThreshold) continue;
            levels[count] = nl;
            which[count] = i;
            count++;
            total += nl;
        }
        if (count == 0) return false;
        int avg = total / (count + 1);
        if (avg < def.MinLevel)
        {
            int best = 0;
            for (int i = 1; i < count; i++)
                if (levels[i] < levels[best]) best = i;
            int pair = level + levels[best];
            if (pair / 2 < def.MinLevel) return false;
            levels[0] = levels[best];
            which[0] = which[best];
            count = 1;
            total = pair;
            avg = pair / 2;
        }
        var type = (LiquidType)def.Code;
        for (int i = 0; i < count; i++)
        {
            if (levels[i] == avg) continue;
            var n = cell + LiquidRules.Horizontal[which[i]];
            Write(batch, n, LiquidCode.Pack(type, avg));
            _received.Add(n);
        }
        Write(batch, cell, LiquidCode.Pack(type, total - avg * count));
        return true;
    }

    private void Write(EditBatch batch, Int3 cell, byte liquid)
    {
        if (batch.GetLiquid(cell) == liquid) return;
        batch.SetLiquid(cell, liquid);
        _changed.Add(cell);
    }
}
