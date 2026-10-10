using System.Numerics;
using Strata.Core.Determinism;
using Strata.Core.Diagnostics;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// Plans the cave network (part of the caves pass's plan, see docs/architecture/worldgen.md "Cave network"): rooms of
/// distinct kinds on four levels (the underground, the upper caverns, the deep caverns, the abyss over the underworld),
/// each level's rooms linked by walkable passages (a spanning tree plus loops), switchback descents from level to level,
/// walk-in entrances from the surface, and dead-end spurs to treasure niches. Pure plan data, deterministic from the
/// seed tree (every stream named, so nothing here shifts other passes).
/// </summary>
internal sealed partial class CaveNetworkPlanner
{
    private readonly PlanContext _ctx;
    private readonly WorldPlan _plan;
    private readonly SeedTree _seeds;
    private readonly List<CaveRoomFeature>[] _rooms = new List<CaveRoomFeature>[CaveTuning.Levels];
    private readonly int[][] _component = new int[CaveTuning.Levels][];
    private readonly List<CaveRoomFeature> _minor = new();
    private readonly List<CavePassageFeature> _passages = new();
    private readonly float _heightScale;
    private long _serial;

    private CaveNetworkPlanner(PlanContext ctx, SeedTree seeds)
    {
        _ctx = ctx;
        _plan = ctx.Plan;
        _seeds = seeds;
        for (int l = 0; l < CaveTuning.Levels; l++) _rooms[l] = new List<CaveRoomFeature>();
        // Shorter worlds (Tiny) keep their levels apart by lowering the rooms.
        _heightScale = Math.Clamp((_plan.Bands.SeaLevel - _plan.Bands.UnderworldTop) / 399f, 0.6f, 1f);
    }

    /// <summary>Plans the network and adds its rooms and passages to the plan's features.</summary>
    public static void Plan(PlanContext ctx, SeedTree seeds)
    {
        var p = new CaveNetworkPlanner(ctx, seeds);
        for (int l = 0; l < CaveTuning.Levels; l++) p.PlaceRooms(l);
        for (int l = 0; l < CaveTuning.Levels; l++) p.ConnectLevel(l);
        for (int l = 1; l < CaveTuning.Levels; l++) p.LinkLevels(l);
        if (!ctx.Plan.Bands.Inverted) p.AddEntrances();
        p.AddSpursAndAlcoves();
        var all = p._rooms.SelectMany(r => r).Concat(p._minor).ToList();
        CaveFurnisher.Furnish(ctx, seeds.Child("furnish"), all);
        foreach (var room in all) ctx.Plan.AddFeature(room);
        foreach (var passage in p._passages) ctx.Plan.AddFeature(passage);
        ctx.Log.Debug("worldgen", $"Cave network: {p._rooms.Sum(r => r.Count)} rooms, {p._minor.Count} small rooms, {p._passages.Count} passage stretches.");
    }

    private ulong NextSeed() => Hash.Combine(_seeds.Value, _serial++);

    /// <summary>One candidate room per square of a jittered grid; candidates off land, near the spawn or without rock round them are dropped.</summary>
    private void PlaceRooms(int level)
    {
        var size = _plan.Size;
        float s = CaveTuning.Spacing[level];
        var grid = _seeds.Child("grid").Child(level).Rng();
        float ox = grid.Range(-s, 0f), oz = grid.Range(-s, 0f);
        int nx = (int)MathF.Ceiling((size.X - ox) / s), nz = (int)MathF.Ceiling((size.Z - oz) / s);
        for (int gz = 0; gz < nz; gz++)
            for (int gx = 0; gx < nx; gx++)
            {
                var rng = _seeds.Child("room").Child(gx, level, gz).Rng();
                float x = ox + (gx + rng.Range(0.15f, 0.85f)) * s, z = oz + (gz + rng.Range(0.15f, 0.85f)) * s;
                if (TryRoom(level, x, z, ref rng) is { } room && !Overlaps(room, _rooms[level])) _rooms[level].Add(room);
            }
    }

    /// <summary>Whether a new room would crowd one already on its level (their footprints overlapping by much).</summary>
    private static bool Overlaps(CaveRoomFeature room, List<CaveRoomFeature> placed)
    {
        float r = MathF.Max(room.RadiusX, room.RadiusZ);
        foreach (var o in placed)
        {
            float reach = (r + MathF.Max(o.RadiusX, o.RadiusZ)) * 0.8f;
            if (Horizontal(room.Center, o.Center) < reach) return true;
        }
        return false;
    }

    private CaveRoomFeature? TryRoom(int level, float x, float z, ref Pcg32 rng)
    {
        var size = _plan.Size;
        const float edge = 72f;
        if (x < edge || z < edge || x > size.X - edge || z > size.Z - edge) return null;
        if (_plan.Landness.Sample(x, z) < CaveTuning.MinLandness) return null;
        var kind = PickKind(level, ref rng);
        var (rx, rz, h, roof, sink) = Shape(kind, level, ref rng);
        float yaw = rng.NextFloat();
        float reach = MathF.Max(rx, rz) + 10f;
        for (int k = 0; k < 8; k++)
            if (_plan.Landness.Sample(x + Trig.Cos(k / 8f) * reach, z + Trig.Sin(k / 8f) * reach) < CaveTuning.MinLandness - 0.02f) return null;
        var bands = _plan.Bands;
        if (level == 0 && !bands.Inverted)
        {
            float sx = x - _plan.Spawn.X, sz = z - _plan.Spawn.Z;
            if (sx * sx + sz * sz < CaveTuning.SpawnClearance * CaveTuning.SpawnClearance) return null;
        }
        int floor = FloorFor(level, x, z, reach, h, ref rng);
        if (bands.Inverted && !InRock(x, z, reach, floor - sink - CaveTuning.RoomFloorRock, floor + h + 4f)) return null;
        return new CaveRoomFeature(x, z, floor, rx, rz, h, yaw, sink)
        {
            Kind = "cave_room", Room = kind, Level = level, Roof = roof, Seed = NextSeed(),
        };
    }

    /// <summary>
    /// The floor row of a new room: underground rooms keep <see cref="CaveTuning.RoofRock"/> or more under the lowest
    /// ground over them (within the underground layer's depths); deeper levels pick a depth in their band.
    /// </summary>
    private int FloorFor(int level, float x, float z, float reach, float h, ref Pcg32 rng)
    {
        var bands = _plan.Bands;
        if (level == 0 && !bands.Inverted)
        {
            float surface = _ctx.Terrain.SurfaceHeight(x, z);
            for (int k = 0; k < 8; k++)
                surface = MathF.Min(surface, _ctx.Terrain.SurfaceHeight(x + Trig.Cos(k / 8f) * reach, z + Trig.Sin(k / 8f) * reach));
            float floor = bands.YForNormalizedDepth(rng.Range(CaveTuning.DepthMin[0], CaveTuning.DepthMax[0]));
            floor = MathF.Min(floor, surface - CaveTuning.RoofRock - h);
            return (int)MathF.Floor(MathF.Max(floor, bands.YForNormalizedDepth(CaveTuning.DepthMax[0])));
        }
        return (int)MathF.Floor(bands.YForNormalizedDepth(rng.Range(CaveTuning.DepthMin[level], CaveTuning.DepthMax[level])));
    }

    /// <summary>Don't Dig Up: whether rock surrounds a room from <paramref name="bottom"/> to <paramref name="top"/> (no open air or roof breach).</summary>
    private bool InRock(float x, float z, float reach, float bottom, float top)
    {
        for (int k = 0; k <= 8; k++)
        {
            float px = k == 8 ? x : x + Trig.Cos(k / 8f) * reach, pz = k == 8 ? z : z + Trig.Sin(k / 8f) * reach;
            if (_ctx.Terrain.SurfaceDepthAt(px, bottom, pz) < 8f || _ctx.Terrain.SurfaceDepthAt(px, top, pz) < 8f) return false;
        }
        return true;
    }

    private static CaveRoomKind PickKind(int level, ref Pcg32 rng)
    {
        float r = rng.NextFloat();
        return level switch
        {
            0 => r < 0.45f ? CaveRoomKind.Hall : r < 0.7f ? CaveRoomKind.Gallery : CaveRoomKind.Grotto,
            1 => r < 0.3f ? CaveRoomKind.Dome : r < 0.52f ? CaveRoomKind.Lake : r < 0.75f ? CaveRoomKind.Terraces : CaveRoomKind.Grotto,
            2 => r < 0.35f ? CaveRoomKind.Pillars : r < 0.6f ? CaveRoomKind.Crystal : r < 0.8f ? CaveRoomKind.Dome : CaveRoomKind.Lake,
            _ => r < 0.6f ? CaveRoomKind.Magma : CaveRoomKind.Pillars,
        };
    }

    /// <summary>Footprint radii, roof height, roof flatness and floor sink of a room kind.</summary>
    private (float Rx, float Rz, float H, int Roof, int Sink) Shape(CaveRoomKind kind, int level, ref Pcg32 rng)
    {
        float hs = _heightScale;
        switch (kind)
        {
            case CaveRoomKind.Hall:
                return (rng.Range(22f, 34f), rng.Range(12f, 18f), rng.Range(11f, 16f) * hs, 3, 0);
            case CaveRoomKind.Gallery:
                return (rng.Range(32f, 48f), rng.Range(8f, 11f), rng.Range(10f, 14f) * hs, 2, 0);
            case CaveRoomKind.Grotto:
            {
                float r = level == 0 ? rng.Range(16f, 24f) : rng.Range(20f, 30f);
                return (r, r * rng.Range(0.8f, 1f), (level == 0 ? rng.Range(10f, 13f) : rng.Range(12f, 17f)) * hs, 3, 4);
            }
            case CaveRoomKind.Dome:
            {
                float r = level <= 1 ? rng.Range(22f, 32f) : rng.Range(28f, 38f);
                return (r, r * rng.Range(0.85f, 1f), (level <= 1 ? rng.Range(24f, 32f) : rng.Range(30f, 42f)) * hs, 2, 0);
            }
            case CaveRoomKind.Lake:
                return (rng.Range(28f, 40f), rng.Range(20f, 30f), rng.Range(15f, 21f) * hs, 3, 7);
            case CaveRoomKind.Terraces:
            {
                float r = rng.Range(25f, 34f);
                return (r, r * rng.Range(0.8f, 0.95f), rng.Range(17f, 23f) * hs, 3, 9);
            }
            case CaveRoomKind.Crystal:
            {
                float r = rng.Range(17f, 25f);
                return (r, r * rng.Range(0.8f, 1f), rng.Range(14f, 20f) * hs, 2, 0);
            }
            case CaveRoomKind.Pillars:
                return (rng.Range(38f, 54f), rng.Range(28f, 38f), rng.Range(24f, 32f) * hs, 4, 0);
            default:
                return (rng.Range(28f, 44f), rng.Range(22f, 34f), rng.Range(16f, 24f) * hs, 3, 3);
        }
    }

    /// <summary>Links a level's rooms: each to its nearest neighbours, keeping a spanning tree and a share of the loops.</summary>
    private void ConnectLevel(int level)
    {
        var rooms = _rooms[level];
        int n = rooms.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        float reach = CaveTuning.Spacing[level] * CaveTuning.LinkReach;
        var edges = new List<(float D, int I, int J)>();
        var seen = new HashSet<long>();
        var near = new List<(float D, int J)>();
        for (int i = 0; i < n; i++)
        {
            near.Clear();
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                float d = Horizontal(rooms[i].Center, rooms[j].Center);
                if (d <= reach) near.Add((d, j));
            }
            near.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.J.CompareTo(b.J));
            for (int k = 0; k < Math.Min(CaveTuning.Neighbours, near.Count); k++)
            {
                int a = Math.Min(i, near[k].J), b = Math.Max(i, near[k].J);
                if (seen.Add((long)a * n + b)) edges.Add((near[k].D, a, b));
            }
        }
        edges.Sort((x, y) => x.D != y.D ? x.D.CompareTo(y.D) : x.I != y.I ? x.I.CompareTo(y.I) : x.J.CompareTo(y.J));
        var rng = _seeds.Child("links").Child(level).Rng();
        foreach (var (_, i, j) in edges)
        {
            bool tree = Union(parent, i, j);
            if (!tree && (!rng.Chance(CaveTuning.LoopChance) || Crowded(rooms[i], rooms[j]) || Crowded(rooms[j], rooms[i]))) continue;
            Connect(rooms[i], rooms[j], level, ref rng);
        }
        _component[level] = new int[n];
        for (int i = 0; i < n; i++) _component[level][i] = Find(parent, i);
    }

    /// <summary>Joins each lower level to the one above: switchback descents from the nearest room above, at least one per
    /// connected group of lower rooms, so every room is reachable from every other.</summary>
    private void LinkLevels(int level)
    {
        var upper = _rooms[level - 1];
        var lower = _rooms[level];
        if (upper.Count == 0 || lower.Count == 0) return;
        var comp = _component[level];
        var linked = new HashSet<int>();
        var rng = _seeds.Child("descents").Child(level).Rng();
        float reach = CaveTuning.Spacing[level] * 2.5f;
        for (int i = 0; i < lower.Count; i++)
        {
            if (!rng.Chance(CaveTuning.DescentChance)) continue;
            var (q, d) = Nearest(upper, lower[i]);
            if (d > reach) continue;
            Connect(upper[q], lower[i], level, ref rng);
            linked.Add(comp[i]);
        }
        foreach (int c in comp.Distinct().ToList())
        {
            if (linked.Contains(c)) continue;
            int best = -1, bestQ = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < lower.Count; i++)
            {
                if (comp[i] != c) continue;
                var (q, d) = Nearest(upper, lower[i]);
                if (d < bestD) (best, bestQ, bestD) = (i, q, d);
            }
            Connect(upper[bestQ], lower[best], level, ref rng);
            linked.Add(c);
        }
    }

    private static (int Index, float Distance) Nearest(List<CaveRoomFeature> rooms, CaveRoomFeature to)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            float d = Horizontal(rooms[i].Center, to.Center);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return (best, bestD);
    }

    /// <summary>Whether a new passage from <paramref name="room"/> towards <paramref name="other"/> would leave within 25° of an existing one.</summary>
    private static bool Crowded(CaveRoomFeature room, CaveRoomFeature other)
    {
        var (ax, az) = Unit(other.X - room.X, other.Z - room.Z);
        foreach (var door in room.Doors)
        {
            var (bx, bz) = Unit(door.X - room.X, door.Z - room.Z);
            if (ax * bx + az * bz > 0.906f) return true;
        }
        return false;
    }

    internal static (float X, float Z) Unit(float dx, float dz)
    {
        float len = MathF.Sqrt(dx * dx + dz * dz);
        return len > 1e-4f ? (dx / len, dz / len) : (1f, 0f);
    }

    internal static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i) i = parent[i] = parent[parent[i]];
        return i;
    }

    private static bool Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra == rb) return false;
        if (ra < rb) parent[rb] = ra;
        else parent[ra] = rb;
        return true;
    }
}
