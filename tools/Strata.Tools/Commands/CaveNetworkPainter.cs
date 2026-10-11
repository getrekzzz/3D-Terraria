using Strata.Core.WorldGen;
using Strata.Core.WorldGen.Caves;

namespace Strata.Tools.Commands;

/// <summary>
/// The cave network seen from above (cave_network.png): rooms as their footprints and passages as lines, coloured by
/// level (underground green, upper caverns blue, deep caverns violet, abyss orange), entrances white, planned chests
/// yellow, over the darkened biome map; plus the network's counts for stats.json.
/// </summary>
internal static class CaveNetworkPainter
{
    private static readonly (float R, float G, float B)[] LevelColors = { (90, 220, 120), (80, 160, 255), (190, 110, 255), (255, 140, 40) };

    public static object Paint(WorldGenerator gen, string path)
    {
        var size = gen.Plan.Size;
        int scale = Math.Max(1, size.X / 1024);
        var img = new ReportPainter.Image(size.X / scale, size.Z / scale);
        int sea = gen.Plan.Bands.SeaLevel;
        var biomeColors = gen.Content.Biomes.All.Select(b => ReportPainter.Parse(b.MapColor)).ToArray();
        for (int pz = 0; pz < img.H; pz++)
            for (int px = 0; px < img.W; px++)
            {
                float x = px * scale, z = pz * scale;
                var c = gen.Terrain.SurfaceHeight(x, z) < sea - 1 ? ((byte)30, (byte)70, (byte)140) : biomeColors[gen.Biomes.SurfaceBiome(x, z)];
                img.Set(px, pz, c.Item1 * 0.3f, c.Item2 * 0.3f, c.Item3 * 0.3f);
            }
        var rooms = gen.Plan.Features.All.OfType<CaveRoomFeature>().ToList();
        var passages = gen.Plan.Features.All.OfType<CavePassageFeature>().ToList();
        foreach (var p in passages)
        {
            var (r, g, b) = LevelColors[Math.Clamp(p.Level, 0, LevelColors.Length - 1)];
            img.Line(p.A.X / scale, p.A.Z / scale, p.B.X / scale, p.B.Z / scale, r * 0.8f, g * 0.8f, b * 0.8f);
        }
        foreach (var room in rooms)
        {
            var (r, g, b) = LevelColors[Math.Clamp(room.Level, 0, LevelColors.Length - 1)];
            float reach = MathF.Max(room.RadiusX, room.RadiusZ);
            for (float dz = -reach; dz <= reach; dz += scale)
                for (float dx = -reach; dx <= reach; dx += scale)
                    if (room.Footprint(room.X + dx, room.Z + dz) < 1f) img.Set((int)((room.X + dx) / scale), (int)((room.Z + dz) / scale), r, g, b);
            foreach (var t in room.Treasure.Where(t => t.Object != "pot")) img.Dot(t.Anchor.X / scale, t.Anchor.Z / scale, 1, 255, 230, 60);
        }
        var bands = gen.Plan.Bands;
        int entrances = 0;
        foreach (var p in passages)
        {
            if (p.A.Y < gen.Terrain.SurfaceHeight(p.A.X, p.A.Z) - 3f) continue;
            img.Dot((int)(p.A.X / scale), (int)(p.A.Z / scale), 2, 255, 255, 255);
            entrances++;
        }
        img.Save(path);
        var main = rooms.Where(r => r.Room is not (CaveRoomKind.Landing or CaveRoomKind.Niche or CaveRoomKind.Alcove)).ToList();
        return new
        {
            roomsByLevel = main.GroupBy(r => r.Level).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            roomsByKind = rooms.GroupBy(r => r.Room).OrderBy(g => g.Key.ToString()).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            passageStretches = passages.Count,
            passageLength = (int)passages.Sum(p => p.Length),
            steepestPassage = Math.Round(passages.Count > 0 ? passages.Max(p => p.Slope) : 0, 3),
            entrances,
            plannedTreasure = rooms.SelectMany(r => r.Treasure).GroupBy(t => t.Object).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            nearestEntrances = passages.Where(p => p.A.Y >= gen.Terrain.SurfaceHeight(p.A.X, p.A.Z) - 3f)
                .OrderBy(p => (p.A.X - gen.Plan.Spawn.X) * (p.A.X - gen.Plan.Spawn.X) + (p.A.Z - gen.Plan.Spawn.Z) * (p.A.Z - gen.Plan.Spawn.Z))
                .Take(3)
                .Select(p => new { x = (int)p.A.X, y = (int)p.A.Y, z = (int)p.A.Z, towardX = Math.Round(p.B.X - p.A.X, 1), towardY = Math.Round(p.B.Y - p.A.Y, 1), towardZ = Math.Round(p.B.Z - p.A.Z, 1) })
                .ToList(),
        };
    }
}
