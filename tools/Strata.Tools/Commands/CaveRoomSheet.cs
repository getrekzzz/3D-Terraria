using System.Collections.Concurrent;
using Strata.Core.Common;
using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Caves;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Tools.Commands;

/// <summary>
/// Close-ups of the cave network (rooms/room_*.png): for the room of each kind nearest spawn, a vertical section along
/// its long axis over a plan slice two rows above its floor (chests yellow, pots tan), drawn at 3 pixels per cell; plus
/// each room's position and size for stats.json (to aim screenshots).
/// </summary>
internal static class CaveRoomSheet
{
    private const int Px = 3, Margin = 10;

    public static object Paint(WorldGenerator gen, ReportPainter painter, string dir)
    {
        Directory.CreateDirectory(dir);
        var spawn = gen.Plan.Spawn;
        var picks = gen.Plan.Features.All.OfType<CaveRoomFeature>()
            .GroupBy(r => r.Room)
            .Select(g => g.OrderBy(r => DistanceSquared(r, spawn.X, spawn.Z)).First())
            .OrderBy(r => r.Level).ThenBy(r => r.Room.ToString())
            .ToList();
        var result = new List<object>();
        foreach (var room in picks)
        {
            var chunks = Around(gen, room);
            float reach = MathF.Max(room.RadiusX, room.RadiusZ) * 1.15f + Margin;
            float ax = Trig(room.Yaw, out float az);
            int w = (int)MathF.Ceiling(reach * 2), lo = room.FloorY - room.Sink - Margin, hi = room.FloorY + (int)room.Height + Margin + 4;
            int sectionH = hi - lo, planH = w;
            var img = new ReportPainter.Image(w * Px, (sectionH + planH + 2) * Px);
            for (int a = 0; a < w; a++)
            {
                float t = a - reach;
                int x = (int)MathF.Floor(room.X + ax * t), z = (int)MathF.Floor(room.Z + az * t);
                for (int y = lo; y < hi; y++) Cell(img, painter, chunks, x, y, z, a, hi - 1 - y);
            }
            int planY = room.FloorY + 2;
            for (int b = 0; b < planH; b++)
                for (int a = 0; a < w; a++)
                {
                    float u = a - reach, v = b - reach;
                    int x = (int)MathF.Floor(room.X + ax * u - az * v), z = (int)MathF.Floor(room.Z + az * u + ax * v);
                    Cell(img, painter, chunks, x, planY, z, a, sectionH + 2 + b);
                }
            foreach (var chunk in chunks.Values)
                foreach (var o in chunk.Objects)
                {
                    bool chest = o.Def.Interaction == Strata.Core.Content.Defs.TileInteraction.Chest;
                    if ((!chest && o.Def.Id != "pot") || o.Anchor.Y < room.FloorY - room.Sink - 2 || o.Anchor.Y > room.FloorY + room.Height) continue;
                    float dx = o.Anchor.X + 1 - room.X, dz = o.Anchor.Z + 1 - room.Z;
                    float u = dx * ax + dz * az, v = -dx * az + dz * ax;
                    if (MathF.Abs(u) >= reach || MathF.Abs(v) >= reach) continue;
                    var (r, g, bl) = chest ? (255f, 230f, 60f) : (200f, 150f, 100f);
                    img.Dot((int)((u + reach) * Px), (int)((sectionH + 2 + v + reach) * Px), chest ? 4 : 2, r, g, bl);
                }
            string name = $"room_L{room.Level}_{room.Room.ToString().ToLowerInvariant()}.png";
            img.Save(Path.Combine(dir, name));
            result.Add(new
            {
                kind = room.Room.ToString(), level = room.Level, x = (int)room.X, z = (int)room.Z, floorY = room.FloorY,
                radiusX = Math.Round(room.RadiusX, 1), radiusZ = Math.Round(room.RadiusZ, 1), yaw = Math.Round(room.Yaw, 3),
                height = Math.Round(room.Height, 1), sink = room.Sink, doors = room.Doors.Count, image = name,
                treasure = room.Treasure.Select(t => t.Object).ToList(),
            });
        }
        return result;
    }

    private static void Cell(ReportPainter.Image img, ReportPainter painter, Dictionary<Int3, GeneratedChunk> chunks, int x, int y, int z, int px, int py)
    {
        if (!chunks.TryGetValue(new Int3(x >> 5, y >> 5, z >> 5), out var chunk)) return;
        var (r, g, b) = painter.CellColor(chunk.Data, ChunkMath.Index(x & 31, y & 31, z & 31), y, x, z);
        for (int j = 0; j < Px; j++)
            for (int i = 0; i < Px; i++) img.Set(px * Px + i, py * Px + j, r, g, b);
    }

    private static Dictionary<Int3, GeneratedChunk> Around(WorldGenerator gen, CaveRoomFeature room)
    {
        var b = room.Bounds;
        var counts = gen.Plan.Settings.ChunkCounts;
        var coords = new List<Int3>();
        for (int cy = Math.Max(0, (b.Min.Y - Margin) >> 5); cy <= Math.Min(counts.Y - 1, (b.Max.Y + Margin) >> 5); cy++)
            for (int cz = Math.Max(0, (b.Min.Z - Margin) >> 5); cz <= Math.Min(counts.Z - 1, (b.Max.Z + Margin) >> 5); cz++)
                for (int cx = Math.Max(0, (b.Min.X - Margin) >> 5); cx <= Math.Min(counts.X - 1, (b.Max.X + Margin) >> 5); cx++)
                    coords.Add(new Int3(cx, cy, cz));
        var result = new ConcurrentDictionary<Int3, GeneratedChunk>();
        Parallel.ForEach(coords, c => result[c] = gen.Generate(c));
        return new Dictionary<Int3, GeneratedChunk>(result);
    }

    private static float Trig(float turns, out float sin)
    {
        sin = Strata.Core.Determinism.Trig.Sin(turns);
        return Strata.Core.Determinism.Trig.Cos(turns);
    }

    private static float DistanceSquared(CaveRoomFeature r, int x, int z) => (r.X - x) * (r.X - x) + (r.Z - z) * (r.Z - z);
}
