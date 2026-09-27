using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Navigation;

/// <summary>Body limits for path finding, in cells.</summary>
public sealed record NavAgent
{
    /// <summary>Clearance height (a 1.8 m body needs 4 cells).</summary>
    public int Height { get; init; } = 4;

    /// <summary>Highest ledge climbed by stepping (no jump).</summary>
    public int StepUp { get; init; } = 1;

    /// <summary>Highest ledge reached by jumping.</summary>
    public int JumpUp { get; init; } = 2;

    /// <summary>Longest drop taken willingly.</summary>
    public int MaxDrop { get; init; } = 6;

    /// <summary>Closed doors count as passable (the walker opens them).</summary>
    public bool OpensDoors { get; init; } = true;

    /// <summary>Cells holding a liquid with contact damage (lava) are avoided.</summary>
    public bool AvoidHarmfulLiquids { get; init; } = true;

    public static readonly NavAgent Humanoid = new();
}

/// <summary>
/// A* over standing positions in the voxel grid. A node is a grid vertex (x, z) with the feet at cell height y: the
/// body occupies the 2×2 cell columns around the vertex, <see cref="NavAgent.Height"/> cells tall, and needs ground
/// under at least one of them. Moves walk the 8 directions, step or jump up ledges, and drop down. Unloaded cells
/// count as solid, so paths never lead into the unknown.
/// </summary>
public sealed class VoxelNavigator
{
    private static readonly Int3[] Directions =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(1, 0, -1), new(-1, 0, 1), new(-1, 0, -1),
    };

    private readonly VoxelWorld _world;

    public VoxelNavigator(VoxelWorld world) => _world = world;

    /// <summary>Nodes expanded by the last search (diagnostics, budgets).</summary>
    public int LastExpanded { get; private set; }

    /// <summary>
    /// Path from <paramref name="start"/> to <paramref name="goal"/> (both nodes), inclusive. When the goal is out of
    /// reach within <paramref name="maxNodes"/> expansions, returns the path to the node that got closest if
    /// <paramref name="allowPartial"/>, else null.
    /// </summary>
    public List<Int3>? FindPath(Int3 start, Int3 goal, NavAgent agent, int maxNodes = 6000, bool allowPartial = false)
    {
        LastExpanded = 0;
        if (!IsStandable(start, agent)) return null;
        var open = new PriorityQueue<Int3, float>();
        var cost = new Dictionary<Int3, float> { [start] = 0 };
        var parent = new Dictionary<Int3, Int3>();
        var closed = new HashSet<Int3>();
        open.Enqueue(start, Heuristic(start, goal));
        var best = start;
        float bestH = Heuristic(start, goal);
        while (open.Count > 0 && LastExpanded < maxNodes)
        {
            var node = open.Dequeue();
            if (!closed.Add(node)) continue;
            LastExpanded++;
            if (node == goal) return Build(parent, node);
            float h = Heuristic(node, goal);
            if (h < bestH)
            {
                bestH = h;
                best = node;
            }
            float g = cost[node];
            foreach (var (next, step) in Neighbours(node, agent))
            {
                if (closed.Contains(next)) continue;
                float ng = g + step;
                if (cost.TryGetValue(next, out float old) && old <= ng) continue;
                cost[next] = ng;
                parent[next] = node;
                open.Enqueue(next, ng + Heuristic(next, goal));
            }
        }
        return allowPartial && best != start ? Build(parent, best) : null;
    }

    /// <summary>Can the agent stand at this node (clear body, ground below)?</summary>
    public bool IsStandable(Int3 node, NavAgent agent) => IsClear(node, agent) && HasGround(node);

    /// <summary>The standable node nearest to <paramref name="around"/> within a cube of <paramref name="radius"/>.</summary>
    public Int3? NearestStandable(Int3 around, NavAgent agent, int radius)
    {
        Int3? best = null;
        int bestD = int.MaxValue;
        for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                {
                    int d = x * x + y * y * 2 + z * z;
                    if (d >= bestD) continue;
                    var n = around + new Int3(x, y, z);
                    if (!IsStandable(n, agent)) continue;
                    best = n;
                    bestD = d;
                }
        return best;
    }

    /// <summary>Closed doors inside the body at this node (the walker must open them first).</summary>
    public IEnumerable<TileObjectInstance> DoorsAt(Int3 node, NavAgent agent)
    {
        var seen = new HashSet<long>();
        for (int y = 0; y < agent.Height; y++)
            for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                    if (_world.TileObjects.At(node + new Int3(dx, y, dz)) is { IsDoor: true } door && seen.Add(door.Id))
                        yield return door;
    }

    private IEnumerable<(Int3 Node, float Cost)> Neighbours(Int3 node, NavAgent agent)
    {
        foreach (var d in Directions)
        {
            bool diagonal = d.X != 0 && d.Z != 0;
            var flat = node + d;
            if (diagonal)
            {
                // No corner cutting: both orthogonal neighbours must be walkable too.
                if (IsStandable(flat, agent) && IsClear(node + new Int3(d.X, 0, 0), agent) && IsClear(node + new Int3(0, 0, d.Z), agent))
                    yield return (flat, 1.414f);
                continue;
            }
            if (IsClear(flat, agent))
            {
                if (HasGround(flat))
                {
                    yield return (flat, 1f);
                    continue;
                }
                // Walk off the edge and drop to the first ground below.
                for (int k = 1; k <= agent.MaxDrop; k++)
                {
                    var down = flat + new Int3(0, -k, 0);
                    if (!IsClear(down, agent)) break;
                    if (HasGround(down))
                    {
                        yield return (down, 1f + k * 0.25f);
                        break;
                    }
                }
                continue;
            }
            // Blocked ahead: climb onto a ledge if there is headroom above us to rise into.
            for (int k = 1; k <= agent.JumpUp; k++)
            {
                if (!IsClear(node + new Int3(0, k, 0), agent)) break;
                var up = flat + new Int3(0, k, 0);
                if (!IsClear(up, agent)) continue;
                if (HasGround(up)) yield return (up, 1f + (k <= agent.StepUp ? 0.3f : 1f) * k);
                break;
            }
        }
    }

    private bool IsClear(Int3 node, NavAgent agent)
    {
        for (int y = 0; y < agent.Height; y++)
            for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    var c = node + new Int3(dx, y, dz);
                    if (!_world.InBounds(c.X, c.Y, c.Z) || _world.IsSolid(c.X, c.Y, c.Z)) return false;
                    if (_world.TileObjects.At(c) is { BlocksMovement: true } obj && !(agent.OpensDoors && obj.IsDoor)) return false;
                    if (agent.AvoidHarmfulLiquids && _world.GetLiquid(c.X, c.Y, c.Z) is var l and not 0 && _world.Content.LiquidOf(l) is { ContactDamage: > 0 }) return false;
                }
        return true;
    }

    private bool HasGround(Int3 node)
    {
        for (int dz = -1; dz <= 0; dz++)
            for (int dx = -1; dx <= 0; dx++)
            {
                var c = node + new Int3(dx, -1, dz);
                if (_world.GetBlock(c) != VoxelWorld.Unknown && _world.IsSolid(c.X, c.Y, c.Z)) return true;
                if (_world.TileObjects.At(c) is { } obj && obj.Def.Collision is Content.Defs.TileCollision.Solid or Content.Defs.TileCollision.Platform && !obj.IsDoor) return true;
            }
        return false;
    }

    private static float Heuristic(Int3 a, Int3 b)
    {
        int dx = Math.Abs(a.X - b.X), dz = Math.Abs(a.Z - b.Z), dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dz) + 0.414f * Math.Min(dx, dz) + dy * 0.5f;
    }

    private static List<Int3> Build(Dictionary<Int3, Int3> parent, Int3 end)
    {
        var path = new List<Int3> { end };
        while (parent.TryGetValue(end, out var p))
        {
            path.Add(p);
            end = p;
        }
        path.Reverse();
        return path;
    }
}
