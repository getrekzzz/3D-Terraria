using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Mining;

/// <summary>How tools interact with blocks: health, required power and the 3D mining brush.</summary>
public static class MiningRules
{
    /// <summary>Damage every block takes to break (Terraria's 100): a copper pickaxe (35) breaks stone in 3 hits.</summary>
    public const float TileHealth = 100f;

    /// <summary>Damage needed to break a block.</summary>
    public static float Health(BlockDef block) => TileHealth;

    /// <summary>Damage one swing of <paramref name="pickPower"/> deals to <paramref name="block"/> (dirt twice, hardmode ores less).</summary>
    public static float HitDamage(BlockDef block, float pickPower) => pickPower * block.PickDamageScale;

    public static bool CanMine(BlockDef block, int pickPower) =>
        block.Render != BlockRender.None && pickPower >= Math.Max(1, block.PickPower) && !block.HasTag("unbreakable");

    /// <summary>
    /// Cells hit by a brush of edge <paramref name="size"/> aimed at <paramref name="target"/> through the face with
    /// normal <paramref name="normal"/>. A brush of the building grid's size is the aligned grid block containing the
    /// target (the cube the cursor shows, a 1 m shaft when digging down). Larger brushes extend into the surface and,
    /// on the other axes, towards the half of the cell that was hit.
    /// </summary>
    public static IEnumerable<Int3> Brush(Int3 target, Int3 normal, Vector3 hitFraction, int size)
    {
        if (size <= 1)
        {
            yield return target;
            yield break;
        }
        if (size == Building.BlockGrid.Size)
        {
            foreach (var cell in Building.BlockGrid.Cells(target)) yield return cell;
            yield break;
        }
        int lo(int axis, float frac, int n)
        {
            if (n != 0) return n > 0 ? -(size - 1) : 0; // extend away from the face, into the material
            int back = (size - 1) / 2;
            return frac < 0.5f ? -(size - 1 - back) : -back;
        }
        int x0 = lo(0, hitFraction.X, normal.X), y0 = lo(1, hitFraction.Y, normal.Y), z0 = lo(2, hitFraction.Z, normal.Z);
        for (int y = 0; y < size; y++)
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    yield return target + new Int3(x0 + x, y0 + y, z0 + z);
    }
}

/// <summary>Accumulated damage on partially mined cells; damage fades if a cell is left alone.</summary>
public sealed class BlockDamageTracker
{
    private readonly Dictionary<Int3, (float Damage, long Tick)> _damage = new();

    /// <summary>Bumped on every change, so renderers rebuild their crack decals only when needed.</summary>
    public int Version { get; private set; }

    /// <summary>Ticks after which partial damage resets (≈6 s).</summary>
    public int ForgetAfterTicks { get; init; } = 360;

    /// <summary>Adds damage; returns the new total.</summary>
    public float Hit(Int3 cell, float amount, long tick)
    {
        float current = 0;
        if (_damage.TryGetValue(cell, out var d) && tick - d.Tick < ForgetAfterTicks) current = d.Damage;
        current += amount;
        _damage[cell] = (current, tick);
        Version++;
        return current;
    }

    public float DamageAt(Int3 cell, long tick) =>
        _damage.TryGetValue(cell, out var d) && tick - d.Tick < ForgetAfterTicks ? d.Damage : 0f;

    public void Clear(Int3 cell)
    {
        if (_damage.Remove(cell)) Version++;
    }

    /// <summary>Drops stale entries (call occasionally).</summary>
    public void Prune(long tick)
    {
        foreach (var key in _damage.Where(kv => tick - kv.Value.Tick >= ForgetAfterTicks).Select(kv => kv.Key).ToList())
        {
            _damage.Remove(key);
            Version++;
        }
    }

    /// <summary>Every damaged cell with its damage and the tick of its last hit (stale ones included until pruned).</summary>
    public Dictionary<Int3, (float Damage, long Tick)>.Enumerator GetEnumerator() => _damage.GetEnumerator();

    public IEnumerable<(Int3 Cell, float Damage)> Active(long tick) =>
        _damage.Where(kv => tick - kv.Value.Tick < ForgetAfterTicks).Select(kv => (kv.Key, kv.Value.Damage));
}
