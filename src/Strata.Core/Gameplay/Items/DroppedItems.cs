using System.Numerics;
using Strata.Core.Physics;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Items;

/// <summary>An item lying in the world.</summary>
public sealed class DroppedItem
{
    public long Id { get; init; }
    public ItemStack Stack { get; set; }
    public Vector3 Position;
    public Vector3 Velocity;
    public int Age { get; set; }
    public int PickupDelay { get; set; }
    public bool Grounded { get; set; }

    /// <summary>Already transformed by Shimmer (it floats out rather than changing again).</summary>
    public bool Shimmered { get; set; }
}

/// <summary>
/// Simulates dropped items: gravity, voxel collision, merging of nearby identical stacks, magnetism
/// towards collectors and pickup. Positions are meters. Main thread, fixed tick.
/// </summary>
public sealed class DroppedItemSystem
{
    public const float Gravity = 20f;
    public const float MagnetRange = 3.2f;
    public const float PickupRange = 1.0f;
    public const int LifetimeTicks = 60 * 60 * 10;
    private static readonly Vector3 Half = new(0.15f, 0.15f, 0.15f);

    private readonly List<DroppedItem> _items = new();
    private long _nextId = 1;

    public IReadOnlyList<DroppedItem> Items => _items;

    public event Action<DroppedItem>? Spawned;
    public event Action<DroppedItem>? Removed;

    public DroppedItem Spawn(ItemStack stack, Vector3 position, Vector3 velocity, int pickupDelay = 20)
    {
        var item = new DroppedItem { Id = _nextId++, Stack = stack, Position = position, Velocity = velocity, PickupDelay = pickupDelay };
        _items.Add(item);
        Spawned?.Invoke(item);
        return item;
    }

    /// <summary>Magnet and pickup ranges and pull speed (data/config/pickups.json).</summary>
    public PickupConfig Config { get; set; } = new();

    /// <summary>Advances one tick for a collector with room for anything.</summary>
    public void Tick(VoxelWorld world, float dt, Vector3 collector, Func<ItemStack, ItemStack> tryCollect) =>
        Tick(world, dt, collector, _ => true, tryCollect);

    /// <summary>
    /// Advances one tick. <paramref name="collector"/> is the collecting entity's centre; <paramref name="canTake"/> says
    /// whether it has room for any of a stack (an item it cannot take is not pulled and stays on the ground, as in
    /// Terraria; pickups such as Hearts always fit); <paramref name="tryCollect"/> stores a stack and returns what did not
    /// fit. <paramref name="grabBonus"/> (m) widens the magnet (the grabRange stat).
    /// </summary>
    public void Tick(VoxelWorld world, float dt, Vector3 collector, Func<ItemStack, bool> canTake, Func<ItemStack, ItemStack> tryCollect,
        float grabBonus = 0f)
    {
        float magnet = Config.MagnetRange + grabBonus;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            it.Age++;
            if (it.PickupDelay > 0) it.PickupDelay--;
            if (it.Age > LifetimeTicks)
            {
                RemoveAt(i);
                continue;
            }

            var toCollector = collector - it.Position;
            float dist = toCollector.Length();
            if (it.PickupDelay == 0 && dist < magnet && canTake(it.Stack))
            {
                if (dist < Config.PickupRange)
                {
                    var rest = tryCollect(it.Stack);
                    if (rest.IsEmpty)
                    {
                        RemoveAt(i);
                        continue;
                    }
                    it.Stack = rest;
                }
                else
                {
                    it.Velocity = Vector3.Lerp(it.Velocity, Vector3.Normalize(toCollector) * Config.PullSpeed, 0.25f);
                }
            }
            else if (it.Stack.Item?.NoGravity == true)
            {
                it.Velocity *= 0.92f; // souls drift to a stop where they fell and hang there
            }
            else
            {
                it.Velocity.Y -= Gravity * dt;
                if (it.Grounded) it.Velocity *= new Vector3(0.8f, 1f, 0.8f);
            }
            if (world.GetBlock(VoxelMotion.CellOf(it.Position)) == VoxelWorld.Unknown) continue; // chunk unloaded: freeze
            it.Grounded = VoxelMotion.Move(world, ref it.Position, ref it.Velocity, Half, dt);
        }
        MergeNearby();
    }

    private void MergeNearby()
    {
        for (int i = 0; i < _items.Count; i++)
            for (int j = _items.Count - 1; j > i; j--)
            {
                var a = _items[i];
                var b = _items[j];
                if (!a.Stack.CanStackWith(b.Stack) || Vector3.DistanceSquared(a.Position, b.Position) > 1f) continue;
                int moved = Math.Min(b.Stack.Count, a.Stack.SpaceLeft);
                if (moved <= 0) continue;
                a.Stack = a.Stack.WithCount(a.Stack.Count + moved);
                b.Stack = b.Stack.WithCount(b.Stack.Count - moved);
                if (b.Stack.IsEmpty) RemoveAt(j);
            }
    }

    /// <summary>Removes a dropped item (destroyed by lava, despawned by a command).</summary>
    public bool Remove(DroppedItem item)
    {
        int i = _items.IndexOf(item);
        if (i < 0) return false;
        RemoveAt(i);
        return true;
    }

    private void RemoveAt(int i)
    {
        var it = _items[i];
        _items.RemoveAt(i);
        Removed?.Invoke(it);
    }
}
