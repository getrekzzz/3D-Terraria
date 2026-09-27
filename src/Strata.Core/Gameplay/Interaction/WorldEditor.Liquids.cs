using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class WorldEditor
{
    /// <summary>Can liquid sit in this cell (loaded, inside the world, not solid, not a closed door)?</summary>
    public bool IsLiquidOpen(Int3 cell)
    {
        if (!World.InBounds(cell.X, cell.Y, cell.Z)) return false;
        ushort b = World.GetBlock(cell);
        if (b == VoxelWorld.Unknown || _session.Content.Blocks[b].IsSolid) return false;
        var obj = World.TileObjects.At(cell);
        return obj == null || !ObjectInteraction.Blocks(obj);
    }

    /// <summary>
    /// Pours <paramref name="amount"/> levels of a liquid into a cell; what does not fit rises into the cells above.
    /// Refused (returns 0) when the cell is closed or holds a different liquid. Returns levels placed.
    /// </summary>
    public int PourLiquid(Int3 cell, LiquidDef liquid, int amount)
    {
        var batch = World.BeginEdit();
        int placed = 0;
        var type = (LiquidType)liquid.Code;
        for (var c = cell; amount > 0 && IsLiquidOpen(c); c += Int3.Up)
        {
            byte cur = batch.GetLiquid(c);
            if (cur != 0 && cur >> 5 != liquid.Code) break;
            int have = cur & 0x1F;
            int add = Math.Min(amount, LiquidCode.MaxLevel - have);
            if (add <= 0) continue;
            batch.SetLiquid(c, LiquidCode.Pack(type, have + add));
            amount -= add;
            placed += add;
        }
        if (placed > 0) Commit(batch);
        return placed;
    }

    /// <summary>
    /// Removes up to <paramref name="amount"/> levels of the liquid in <paramref name="cell"/>, taking from the
    /// connected pool's surface first (up to <paramref name="searchCells"/> cells). Nothing is taken when the pool
    /// holds less than <paramref name="minimum"/>. Returns what was taken and its liquid.
    /// </summary>
    public (LiquidDef? Liquid, int Levels) DrainLiquid(Int3 cell, int amount, int minimum = 1, int searchCells = 32)
    {
        var liquid = _session.Content.LiquidOf(World.GetLiquid(cell.X, cell.Y, cell.Z));
        if (liquid == null || amount <= 0) return (null, 0);
        var pool = ConnectedLiquid(cell, liquid.Code, searchCells);
        if (pool.Sum(c => World.GetLiquid(c.X, c.Y, c.Z) & 0x1F) < minimum) return (liquid, 0);
        // Highest cells first, so the surface drops rather than the floor emptying.
        pool.Sort((a, b) => b.Y != a.Y ? b.Y.CompareTo(a.Y) : 0);
        var batch = World.BeginEdit();
        int taken = 0;
        foreach (var c in pool)
        {
            if (taken >= amount) break;
            int have = World.GetLiquid(c.X, c.Y, c.Z) & 0x1F;
            int take = Math.Min(have, amount - taken);
            batch.SetLiquid(c, LiquidCode.Pack((LiquidType)liquid.Code, have - take));
            taken += take;
        }
        if (taken > 0) Commit(batch);
        return (liquid, taken);
    }

    /// <summary>Sets every open cell in a box to a liquid level (0 or null liquid clears). Returns cells written.</summary>
    public int FillLiquid(Int3 min, Int3 max, LiquidDef? liquid, int level)
    {
        byte code = liquid == null ? (byte)0 : LiquidCode.Pack((LiquidType)liquid.Code, level);
        var batch = World.BeginEdit();
        int n = 0;
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var c = new Int3(x, y, z);
                    if (code != 0 && !IsLiquidOpen(c)) continue;
                    if (batch.SetLiquid(c, code)) n++;
                }
        Commit(batch);
        return n;
    }

    /// <summary>Same-liquid cells connected to <paramref name="start"/> by faces (breadth first, bounded).</summary>
    private List<Int3> ConnectedLiquid(Int3 start, int code, int limit)
    {
        var found = new List<Int3> { start };
        var seen = new HashSet<Int3> { start };
        for (int i = 0; i < found.Count && found.Count < limit; i++)
            foreach (var o in Faces.Offsets)
            {
                var n = found[i] + o;
                if (!seen.Add(n)) continue;
                byte l = World.GetLiquid(n.X, n.Y, n.Z);
                if (l == 0 || l >> 5 != code) continue;
                found.Add(n);
                if (found.Count >= limit) break;
            }
        return found;
    }
}
