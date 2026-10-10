using System.Numerics;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Caves;

/// <summary>Passages: meandering level runs, switchback descents with landings, surface entrances, spurs and alcoves.</summary>
internal sealed partial class CaveNetworkPlanner
{
    /// <summary>Links two rooms with a passage between doors on their floors, facing each other.</summary>
    private void Connect(CaveRoomFeature a, CaveRoomFeature b, int level, ref Pcg32 rng)
    {
        var da = DoorToward(a, b.X, b.Z);
        var db = DoorToward(b, a.X, a.Z);
        a.Doors.Add(da);
        b.Doors.Add(db);
        Route(da, db, level, ref rng, lead: true);
    }

    /// <summary>A floor point inside a room, most of the way to its wall in the direction of (tx, tz).</summary>
    private static Vector3 DoorToward(CaveRoomFeature room, float tx, float tz)
    {
        var (dx, dz) = Unit(tx - room.X, tz - room.Z);
        return DoorAlong(room, dx, dz, 0.72f);
    }

    private static Vector3 DoorAlong(CaveRoomFeature room, float dx, float dz, float u)
    {
        var (x, z) = room.Along(dx, dz, u);
        return new Vector3(x, room.FloorY, z);
    }

    /// <summary>
    /// Lays a passage from floor point <paramref name="a"/> to <paramref name="b"/>: switchbacks first when the climb is
    /// too steep for a straight run, then a meandering level approach. <paramref name="lead"/>: leave the start room on the
    /// level before descending.
    /// </summary>
    private void Route(Vector3 a, Vector3 b, int level, ref Pcg32 rng, bool lead, float widthScale = 1f)
    {
        float hw = rng.Range(CaveTuning.PassageHalfWidthMin[level], CaveTuning.PassageHalfWidthMax[level]) * widthScale;
        float h = rng.Range(CaveTuning.PassageHeightMin[level], CaveTuning.PassageHeightMax[level]) * MathF.Max(0.85f, _heightScale)
            * (widthScale < 1f ? 0.88f : 1f);
        var points = new List<Vector3> { a };
        if (MathF.Abs(b.Y - a.Y) > CaveTuning.MaxSlope * 0.92f * Horizontal(a, b)) Switchbacks(a, b, hw, h, level, points, ref rng, lead);
        Meander(points[^1], b, points, ref rng);
        for (int i = 1; i < points.Count; i++)
        {
            if (Vector3.DistanceSquared(points[i - 1], points[i]) < 0.01f) continue;
            _passages.Add(new CavePassageFeature(points[i - 1], points[i], hw, h) { Kind = "cave_passage", Level = level, Seed = NextSeed() });
        }
    }

    /// <summary>A level-or-gently-sloping run from <paramref name="p"/> to <paramref name="b"/>, bending from side to side.
    /// Bends only lengthen the run, so the floor never gets steeper than the straight line.</summary>
    private static void Meander(Vector3 p, Vector3 b, List<Vector3> points, ref Pcg32 rng)
    {
        float dx = b.X - p.X, dz = b.Z - p.Z, run = MathF.Sqrt(dx * dx + dz * dz);
        int n = Math.Max(1, (int)MathF.Round(run / CaveTuning.BendEvery));
        if (n > 1)
        {
            float px = -dz / run, pz = dx / run;
            float swing = MathF.Min(run * CaveTuning.BendSwing, 12f), off = 0f;
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)n;
                off = off * 0.5f + rng.Range(-1f, 1f) * swing;
                float w = 4f * t * (1f - t);
                points.Add(new Vector3(p.X + dx * t + px * off * w, p.Y + (b.Y - p.Y) * t, p.Z + dz * t + pz * off * w));
            }
        }
        points.Add(b);
    }

    /// <summary>
    /// Descends (or climbs) from <paramref name="a"/> to <paramref name="b"/>'s height in switchback legs: each leg
    /// <see cref="CaveTuning.LegMin"/>–<see cref="CaveTuning.LegMax"/> long at no more than
    /// <see cref="CaveTuning.LegSlope"/> (rounded to whole rows), swinging alternately to either side of the heading so
    /// the zigzag advances towards <paramref name="b"/>. A landing chamber widens every turn. Appends the leg ends; the
    /// last one is level with <paramref name="b"/>.
    /// </summary>
    private void Switchbacks(Vector3 a, Vector3 b, float hw, float h, int level, List<Vector3> points, ref Pcg32 rng, bool lead)
    {
        int total = (int)MathF.Round(MathF.Abs(b.Y - a.Y));
        if (total == 0) return;
        float sign = b.Y > a.Y ? 1f : -1f;
        float dx = b.X - a.X, dz = b.Z - a.Z, run = MathF.Sqrt(dx * dx + dz * dz);
        float hx, hz;
        if (run > 1f) (hx, hz) = (dx / run, dz / run);
        else
        {
            float turn = rng.NextFloat();
            (hx, hz) = (Trig.Cos(turn), Trig.Sin(turn));
        }
        var p = a;
        if (lead)
        {
            p = new Vector3(a.X + hx * 8f, a.Y, a.Z + hz * 8f);
            points.Add(p);
            run = MathF.Max(0f, run - 8f);
        }
        float leg = rng.Range(CaveTuning.LegMin, CaveTuning.LegMax);
        int legs = Math.Max(1, (int)MathF.Ceiling(total / (leg * CaveTuning.LegSlope)));
        float c = Math.Clamp(MathF.Max(0f, run - 8f) / (legs * leg), Trig.Cos(CaveTuning.LegSwing), 0.85f);
        float s = MathF.Sqrt(1f - c * c);
        float side = rng.Chance(0.5f) ? 1f : -1f;
        int done = 0;
        for (int k = 0; k < legs; k++)
        {
            int drop = total * (k + 1) / legs - done;
            done += drop;
            float ox = hx * c - hz * s * side, oz = hz * c + hx * s * side;
            side = -side;
            p = new Vector3(p.X + ox * leg, p.Y + sign * drop, p.Z + oz * leg);
            points.Add(p);
            if (k < legs - 1) _minor.Add(SmallRoom(CaveRoomKind.Landing, p, hw + rng.Range(2.5f, 4f), h + rng.Range(1.5f, 3.5f), level, ref rng));
        }
    }

    private CaveRoomFeature SmallRoom(CaveRoomKind kind, Vector3 floor, float r, float h, int level, ref Pcg32 rng) =>
        new(floor.X, floor.Z, (int)MathF.Round(floor.Y), r, r * rng.Range(0.8f, 1f), h, rng.NextFloat(), 0)
        {
            Kind = "cave_room", Room = kind, Level = level, Roof = 2, Seed = NextSeed(),
        };

    /// <summary>Walk-in entrances: a switchback from a mouth in the ground down to an underground room, for a share of the
    /// rooms and at least one per connected group of them.</summary>
    private void AddEntrances()
    {
        var rooms = _rooms[0];
        if (rooms.Count == 0) return;
        var comp = _component[0];
        var rng = _seeds.Child("entrances").Rng();
        var served = new HashSet<int>();
        for (int i = 0; i < rooms.Count; i++)
            if (rng.Chance(CaveTuning.EntranceChance) && TryEntrance(rooms[i], ref rng)) served.Add(comp[i]);
        for (int i = 0; i < rooms.Count; i++)
            if (!served.Contains(comp[i]) && TryEntrance(rooms[i], ref rng)) served.Add(comp[i]);
    }

    private bool TryEntrance(CaveRoomFeature room, ref Pcg32 rng)
    {
        var spawn = _plan.Spawn;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float turn = rng.NextFloat();
            float dist = MathF.Max(room.RadiusX, room.RadiusZ) + rng.Range(CaveTuning.EntranceMin, CaveTuning.EntranceMax);
            float x = room.X + Trig.Cos(turn) * dist, z = room.Z + Trig.Sin(turn) * dist;
            if (_plan.Landness.Sample(x, z) < 0.92f) continue;
            float sx = x - spawn.X, sz = z - spawn.Z;
            if (sx * sx + sz * sz < CaveTuning.SpawnClearance * CaveTuning.SpawnClearance) continue;
            float mouth = MathF.Round(_ctx.Terrain.SurfaceHeight(x, z)) - 1f;
            if (mouth < room.FloorY + 12) continue;
            var door = DoorToward(room, x, z);
            room.Doors.Add(door);
            Route(new Vector3(x, mouth, z), door, 0, ref rng, lead: false);
            return true;
        }
        return false;
    }

    /// <summary>Dead-end spurs to treasure niches off a share of the rooms, and alcoves along the walls of the long halls.</summary>
    private void AddSpursAndAlcoves()
    {
        var rng = _seeds.Child("spurs").Rng();
        for (int level = 0; level < CaveTuning.Levels; level++)
            foreach (var room in _rooms[level])
            {
                if (rng.Chance(CaveTuning.SpurChance)) Spur(room, level, ref rng);
                if (room.Room is not (CaveRoomKind.Hall or CaveRoomKind.Gallery or CaveRoomKind.Pillars)) continue;
                for (int k = rng.Range(1, 3); k > 0; k--) Alcove(room, level, ref rng);
            }
    }

    private void Spur(CaveRoomFeature room, int level, ref Pcg32 rng)
    {
        var (dx, dz) = FreeDirection(room, ref rng);
        var door = DoorAlong(room, dx, dz, 0.72f);
        float len = rng.Range(CaveTuning.SpurMin, CaveTuning.SpurMax);
        float reach = room.RadiusToward(dx, dz) * 0.28f + len;
        int rise = (int)MathF.Round(rng.Range(-0.2f, 0.2f) * len);
        float r = rng.Range(5f, 7f);
        var niche = SmallRoom(CaveRoomKind.Niche, new Vector3(door.X + dx * (reach + r * 0.5f), door.Y + rise, door.Z + dz * (reach + r * 0.5f)),
            r, rng.Range(6f, 8f) * MathF.Max(0.85f, _heightScale), level, ref rng);
        var end = DoorToward(niche, room.X, room.Z);
        room.Doors.Add(door);
        niche.Doors.Add(end);
        _minor.Add(niche);
        Route(door, end, level, ref rng, lead: true, widthScale: 0.8f);
    }

    private void Alcove(CaveRoomFeature room, int level, ref Pcg32 rng)
    {
        var (dx, dz) = FreeDirection(room, ref rng);
        var at = DoorAlong(room, dx, dz, 0.97f);
        room.Doors.Add(DoorAlong(room, dx, dz, 0.85f)); // keeps formations out of the alcove's mouth
        _minor.Add(SmallRoom(CaveRoomKind.Alcove, at, rng.Range(5f, 8f), rng.Range(6f, 9f) * _heightScale, level, ref rng));
    }

    private static (float X, float Z) FreeDirection(CaveRoomFeature room, ref Pcg32 rng) =>
        FreeDirection(room, room.Doors.Select(d => (d.X, d.Z)), ref rng);

    /// <summary>Of eight random directions from a room's centre, the one furthest (by angle) from every point to avoid.</summary>
    internal static (float X, float Z) FreeDirection(CaveRoomFeature room, IEnumerable<(float X, float Z)> avoid, ref Pcg32 rng)
    {
        var dirs = avoid.Select(p => Unit(p.X - room.X, p.Z - room.Z)).ToList();
        (float X, float Z) best = (1f, 0f);
        float bestDot = float.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            float turn = rng.NextFloat();
            float x = Trig.Cos(turn), z = Trig.Sin(turn), worst = -1f;
            foreach (var (ux, uz) in dirs) worst = MathF.Max(worst, x * ux + z * uz);
            if (worst < bestDot) (best, bestDot) = ((x, z), worst);
        }
        return best;
    }
}
