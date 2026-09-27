using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Items;
using Strata.Core.Meshing;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Interaction;

public enum ObjectHitResult
{
    /// <summary>The tool cannot affect this object.</summary>
    WrongTool,

    /// <summary>Damaged but still standing.</summary>
    Damaged,
    Broken,

    /// <summary>A container that still holds items.</summary>
    NotEmpty,

    /// <summary>The tool is the right kind but too weak (a Pwnhammer is needed for altars).</summary>
    TooWeak,
}

public sealed partial class WorldEditor
{
    /// <summary>Raised after an object is broken by gameplay (not by chunk unloading).</summary>
    public event Action<TileObjectInstance>? ObjectBroken;

    public event Action<TileObjectInstance>? ObjectPlaced;

    /// <summary>Places a tile object whose minimum corner is <paramref name="anchor"/>.</summary>
    public PlacementResult PlaceObject(TileObjectDef def, Int3 anchor, int rotation, int variant = 0)
    {
        var result = PlacementRules.CanPlaceObject(World, def, anchor, def.Rotatable ? rotation : 0);
        if (result != PlacementResult.Ok) return result;
        var obj = new TileObjectInstance(def, anchor, def.Rotatable ? rotation : 0, variant);
        if (!World.TileObjects.TryAdd(obj)) return PlacementResult.Occupied;
        DirtyForLight(obj);
        ObjectPlaced?.Invoke(obj);
        return PlacementResult.Ok;
    }

    /// <summary>
    /// Hits an object with a tool. <paramref name="power"/> is the matching tool power (axe for trees, pick for
    /// furniture); decor breaks from any hit.
    /// </summary>
    public ObjectHitResult HitObject(TileObjectInstance obj, ItemDef? tool)
    {
        int power = obj.Def.Decor ? 100 : obj.Def.BreakTool switch
        {
            BreakTool.Axe => tool?.AxePower ?? 0,
            BreakTool.Pick => tool?.PickPower ?? 0,
            BreakTool.Hammer => tool?.HammerPower ?? 0,
            _ => Math.Max(tool?.PickPower ?? 0, Math.Max(tool?.AxePower ?? 0, tool?.HammerPower ?? 0)),
        };
        if (power <= 0) return ObjectHitResult.WrongTool;
        if (power < obj.Def.MinPower) return ObjectHitResult.TooWeak;
        if (obj.Def.RequiresFlag is { } flag && !_session.Flags.Contains(flag)) return ObjectHitResult.TooWeak;
        if (obj.Container != null && obj.Container.Any(s => !s.IsEmpty)) return ObjectHitResult.NotEmpty;
        obj.HitPoints -= Math.Max(1, power / 40);
        if (obj.HitPoints > 0) return ObjectHitResult.Damaged;
        BreakObject(obj, dropItems: true);
        return ObjectHitResult.Broken;
    }

    /// <summary>Removes an object, dropping its item, rolled loot and any container contents.</summary>
    public void BreakObject(TileObjectInstance obj, bool dropItems)
    {
        if (!World.TileObjects.Remove(obj)) return;
        DirtyForLight(obj);
        if (dropItems)
        {
            var center = obj.Anchor + new Int3(obj.Footprint.X / 2, 0, obj.Footprint.Z / 2);
            var content = _session.Content;
            if (obj.Def.PlacingItemId is { } itemId && content.Items.TryGet(itemId, out var item) && !obj.Def.Natural)
                SpawnDrop(new ItemStack(item, 1), center);
            if (obj.Def.Loot != null)
            {
                var rng = NextRng(center);
                foreach (var stack in _loot.Roll(obj.Def.Loot, ref rng, LootContext(center))) SpawnDrop(stack, center);
            }
            if (obj.Container != null)
                foreach (var stack in obj.Container.Where(s => !s.IsEmpty)) SpawnDrop(stack, center);
        }
        ObjectBroken?.Invoke(obj);
        // Objects resting on this one (a torch on a chest) may now be unsupported.
        BreakUnsupportedAround(BoundaryCells(obj.Bounds), new List<TileObjectInstance>());
    }

    /// <summary>Rolls a world chest's pending loot into its container the first time it is opened.</summary>
    public void EnsureContainerLoot(TileObjectInstance obj)
    {
        if (obj.Container == null || obj.LootRolled) return;
        obj.LootRolled = true;
        if (obj.PendingLoot == null) return;
        var rng = NextRng(obj.Anchor);
        var inv = new Inventory(obj.Container);
        foreach (var stack in _loot.Roll(obj.PendingLoot, ref rng, LootContext(obj.Anchor))) inv.Add(stack);
        obj.PendingLoot = null;
        var chunk = World.GetChunk(ChunkMath.CellToChunk(obj.Anchor));
        if (chunk != null) chunk.Modified = true;
    }

    private ConditionContext LootContext(Int3 cell) => _session.ContextAt(cell);

    private void BreakUnsupportedAround(IEnumerable<Int3> cells, List<TileObjectInstance> broken)
    {
        var candidates = new HashSet<TileObjectInstance>();
        foreach (var cell in cells)
            foreach (var o in Faces.Offsets)
                if (World.TileObjects.At(cell + o) is { } obj) candidates.Add(obj);
        foreach (var obj in candidates)
        {
            if (World.TileObjects.Get(obj.Id) == null || PlacementRules.IsSupported(World, obj)) continue;
            broken.Add(obj);
            BreakObject(obj, dropItems: true);
        }
    }

    private static IEnumerable<Int3> BoundaryCells(Aabb3i box)
    {
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                    yield return new Int3(x, y, z);
    }

    private void DirtyForLight(TileObjectInstance obj)
    {
        if (obj.Def.Light is not { Length: 3 } l || (l[0] | l[1] | l[2]) == 0) return;
        var box = obj.Bounds.Expand(Neighborhood.Margin);
        var min = ChunkMath.CellToChunk(box.Min);
        var max = ChunkMath.CellToChunk(box.Max - Int3.One);
        var dirty = new HashSet<Int3>();
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                    if (World.HasChunk(new Int3(x, y, z))) dirty.Add(new Int3(x, y, z));
        if (dirty.Count > 0) ChunksDirty?.Invoke(dirty);
    }
}
