using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;

namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// Dresses the planned rooms (plan data only; <see cref="CaveCarver"/> builds it): pools sunk into the floor, rock and
/// crystal formations, and treasure: a chest by level and biome where the kind calls for one, and clusters of pots.
/// Formations keep clear of doors and treasure, so passages and chests stay reachable.
/// </summary>
internal static class CaveFurnisher
{
    public static void Furnish(PlanContext ctx, SeedTree seeds, List<CaveRoomFeature> rooms)
    {
        foreach (var room in rooms)
        {
            var rng = seeds.Child(unchecked((long)room.Seed)).Rng();
            string biome = ctx.Biomes.IdOf(ctx.Biomes.BiomeAt(room.X, room.FloorY + 2, room.Z));
            Pools(room, ref rng);
            Treasure(room, biome, ref rng);
            Formations(room, Rock(room, biome), ref rng);
        }
    }

    /// <summary>The rock formations grow from: the biome's own (ice, sandstone, evil stone) or the layer's stone.</summary>
    private static string Rock(CaveRoomFeature room, string biome) => biome switch
    {
        "tundra" => "ice",
        "desert" or "underground_desert" => "sandstone",
        "corruption" => "ebonstone",
        "crimson" => "crimstone",
        _ => room.Level == 0 ? "stone" : "deepstone",
    };

    private static void Pools(CaveRoomFeature room, ref Pcg32 rng)
    {
        float small = MathF.Min(room.RadiusX, room.RadiusZ);
        switch (room.Room)
        {
            case CaveRoomKind.Lake:
                room.Pools.Add(new CavePool(room.X, room.Z, small * 0.66f, room.Sink, LiquidType.Water));
                break;
            case CaveRoomKind.Grotto:
            {
                var (dx, dz) = CaveNetworkPlanner.FreeDirection(room, Doors(room), ref rng);
                var (x, z) = room.Along(dx, dz, 0.3f);
                room.Pools.Add(new CavePool(x, z, small * 0.42f, rng.Range(3, room.Sink), LiquidType.Water));
                break;
            }
            case CaveRoomKind.Magma:
                for (int want = rng.Range(2, 4), tries = 0; want > 0 && tries < 24; tries++)
                {
                    float r = rng.Range(2.5f, 5f);
                    var (x, z) = room.At(MathF.Sqrt(rng.NextFloat()) * 0.55f, rng.NextFloat());
                    if (Near(Doors(room), x, z, r + 6f) || room.Pools.Any(p => Distance(p.X, p.Z, x, z) < p.Radius + r + 3f)) continue;
                    room.Pools.Add(new CavePool(x, z, r, rng.Range(2, room.Sink), LiquidType.Lava));
                    want--;
                }
                break;
        }
    }

    private static void Treasure(CaveRoomFeature room, string biome, ref Pcg32 rng)
    {
        float chest = room.Room switch
        {
            CaveRoomKind.Hall => 0.45f,
            CaveRoomKind.Gallery => 0.5f,
            CaveRoomKind.Grotto => 0.55f,
            CaveRoomKind.Dome or CaveRoomKind.Lake => 0.5f,
            CaveRoomKind.Terraces => 0.6f,
            CaveRoomKind.Crystal => 0.85f,
            CaveRoomKind.Pillars => 0.55f,
            CaveRoomKind.Magma => 0.7f,
            CaveRoomKind.Niche => 1f,
            CaveRoomKind.Alcove => 0.25f,
            _ => 0f,
        };
        var avoid = Doors(room).Concat(room.Pools.Select(p => (p.X, p.Z))).ToList();
        if (rng.Chance(chest))
        {
            float u = room.Room switch
            {
                CaveRoomKind.Lake or CaveRoomKind.Terraces => 0.68f,
                CaveRoomKind.Niche => 0.3f,
                CaveRoomKind.Alcove => 0f,
                _ => 0.6f,
            };
            var (dx, dz) = CaveNetworkPlanner.FreeDirection(room, avoid, ref rng);
            var (x, z) = room.Along(dx, dz, u);
            var (obj, loot) = CaveChests.ForRoom(room, biome);
            room.Treasure.Add(new CaveTreasure(Anchor(x, room.FloorY, z), obj, loot, Facing(dx, dz)));
            avoid.Add((x, z));
        }

        float pots = room.Room switch { CaveRoomKind.Landing => 0.3f, CaveRoomKind.Niche => 0.75f, CaveRoomKind.Alcove => 0.4f, _ => 0.55f };
        if (!rng.Chance(pots)) return;
        bool small = room.Room is CaveRoomKind.Landing or CaveRoomKind.Niche or CaveRoomKind.Alcove;
        int n = room.Room switch { CaveRoomKind.Niche => rng.Range(1, 3), _ when small => rng.Range(1, 2), _ => rng.Range(2, 5) };
        var (px, pz) = CaveNetworkPlanner.FreeDirection(room, avoid, ref rng);
        var (cx, cz) = room.Along(px, pz, small ? 0.45f : room.Room is CaveRoomKind.Lake or CaveRoomKind.Terraces ? 0.7f : 0.72f);
        for (int k = 0; k < n; k++)
        {
            float off = (k - (n - 1) * 0.5f) * 2.6f;
            float x = cx - pz * off, z = cz + px * off;
            if (room.Footprint(x, z) > 0.8f || room.InPool(x, z, out _)) continue;
            room.Treasure.Add(new CaveTreasure(Anchor(x, room.FloorY, z), "pot", null, rng.NextInt(4), rng.NextInt(3)));
        }
    }

    private static void Formations(CaveRoomFeature room, string rock, ref Pcg32 rng)
    {
        float small = MathF.Min(room.RadiusX, room.RadiusZ);
        switch (room.Room)
        {
            case CaveRoomKind.Hall:
            case CaveRoomKind.Gallery:
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(4, 9), 1f, 1.8f, 2.5f, 6f, ref rng);
                Scatter(room, FormationShape.Stalagmite, rock, rng.Range(2, 5), 1.2f, 2.2f, 2f, 4.5f, ref rng);
                Scatter(room, FormationShape.Column, rock, rng.Range(0, 1), 1.6f, 2.4f, 0f, 0f, ref rng);
                break;
            case CaveRoomKind.Grotto:
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(3, 6), 0.9f, 1.4f, 2f, 4f, ref rng);
                Scatter(room, FormationShape.Stalagmite, rock, rng.Range(1, 3), 0.9f, 1.5f, 1.5f, 3f, ref rng);
                break;
            case CaveRoomKind.Dome:
            {
                float r = Math.Clamp(small * 0.2f, 4f, 6.5f);
                room.Formations.Add(new CaveFormation(room.X + rng.Range(-2f, 2f), room.Z + rng.Range(-2f, 2f), r,
                    room.Height * rng.Range(0.3f, 0.45f), FormationShape.Stalagmite, rock));
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(6, 10), 1.2f, 2.2f, 3f, 8f, ref rng);
                Scatter(room, FormationShape.Stalagmite, rock, rng.Range(3, 5), 1.2f, 2f, 2f, 5f, ref rng);
                break;
            }
            case CaveRoomKind.Lake:
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(6, 12), 1f, 2f, 3f, 7f, ref rng);
                Scatter(room, FormationShape.Column, rock, rng.Range(1, 2), 1.8f, 2.8f, 0f, 0f, ref rng, minU: 0.6f);
                break;
            case CaveRoomKind.Terraces:
                Scatter(room, FormationShape.Stalagmite, rock, rng.Range(3, 6), 1f, 1.8f, 2f, 4f, ref rng);
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(4, 8), 1f, 1.8f, 3f, 6f, ref rng);
                break;
            case CaveRoomKind.Crystal:
                Scatter(room, FormationShape.Stalagmite, "crystal_cluster", rng.Range(7, 11), 0.9f, 1.8f, 3f, 8f, ref rng);
                Scatter(room, FormationShape.Stalactite, "crystal_cluster", rng.Range(6, 10), 0.9f, 1.6f, 2.5f, 7f, ref rng);
                break;
            case CaveRoomKind.Pillars:
                Colonnade(room, rock, ref rng);
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(6, 10), 1.2f, 2.2f, 3f, 8f, ref rng);
                break;
            case CaveRoomKind.Magma:
                Scatter(room, FormationShape.Stalagmite, "obsidian", rng.Range(3, 6), 1.5f, 2.6f, 3f, 6f, ref rng);
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(3, 6), 1.2f, 2f, 3f, 6f, ref rng);
                break;
            case CaveRoomKind.Landing:
                Scatter(room, FormationShape.Stalactite, rock, rng.Range(0, 2), 0.8f, 1.2f, 1.5f, 2.5f, ref rng);
                break;
        }
    }

    /// <summary>Places up to <paramref name="count"/> formations at random spots of the floor clear of doors, treasure
    /// and (for anything standing on the floor) pools. Heights are capped by the roof over the spot.</summary>
    private static void Scatter(CaveRoomFeature room, FormationShape shape, string block, int count, float rMin, float rMax,
        float hMin, float hMax, ref Pcg32 rng, float minU = 0f)
    {
        var doors = Doors(room).ToList();
        for (int tries = 0; count > 0 && tries < count * 6; tries++)
        {
            float u = minU + (0.8f - minU) * MathF.Sqrt(rng.NextFloat());
            var (x, z) = room.At(u, rng.NextFloat());
            float r = rng.Range(rMin, rMax), h = rng.Range(hMin, hMax);
            if (!Clear(room, doors, x, z, r, standing: shape != FormationShape.Stalactite)) continue;
            float roof = room.CeilingAt(room.Footprint(x, z));
            if (shape == FormationShape.Column) h = roof;
            else h = MathF.Min(h, roof * (shape == FormationShape.Stalactite ? 0.55f : 0.5f));
            if (h < 1.5f) continue;
            room.Formations.Add(new CaveFormation(x, z, r, h, shape, block));
            count--;
        }
    }

    /// <summary>Stone columns on a jittered grid over the middle of a pillared hall.</summary>
    private static void Colonnade(CaveRoomFeature room, string rock, ref Pcg32 rng)
    {
        var doors = Doors(room).ToList();
        float step = rng.Range(13f, 16f);
        float reach = MathF.Max(room.RadiusX, room.RadiusZ);
        for (float gz = -reach; gz <= reach; gz += step)
            for (float gx = -reach; gx <= reach; gx += step)
            {
                float x = room.X + gx + rng.Range(-2.5f, 2.5f), z = room.Z + gz + rng.Range(-2.5f, 2.5f), r = rng.Range(2.6f, 4.2f);
                float u = room.Footprint(x, z);
                if (u > 0.72f || !Clear(room, doors, x, z, r, standing: true)) continue;
                room.Formations.Add(new CaveFormation(x, z, r, room.CeilingAt(u), FormationShape.Column, rock));
            }
    }

    private static bool Clear(CaveRoomFeature room, List<(float X, float Z)> doors, float x, float z, float r, bool standing)
    {
        if (Near(doors, x, z, r + 6f)) return false;
        foreach (var t in room.Treasure)
            if (Distance(t.Anchor.X + 1f, t.Anchor.Z + 1f, x, z) < r + 2.5f) return false;
        foreach (var f in room.Formations)
            if (Distance(f.X, f.Z, x, z) < f.Radius + r + 1f) return false;
        if (standing)
            foreach (var p in room.Pools)
                if (Distance(p.X, p.Z, x, z) < p.Radius + r + 1f) return false;
        return true;
    }

    private static IEnumerable<(float X, float Z)> Doors(CaveRoomFeature room) => room.Doors.Select(d => (d.X, d.Z));

    private static bool Near(IEnumerable<(float X, float Z)> points, float x, float z, float distance) =>
        points.Any(p => Distance(p.X, p.Z, x, z) < distance);

    private static float Distance(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The anchor (lowest corner) of a 2×2 object centred on a column, standing on a floor row.</summary>
    private static Int3 Anchor(float x, int floorY, float z) => new((int)MathF.Floor(x + 0.5f) - 1, floorY, (int)MathF.Floor(z + 0.5f) - 1);

    /// <summary>The rotation that turns an object by the wall back towards the room's centre (0 faces +Z, 1 −X, 2 −Z, 3 +X).</summary>
    private static int Facing(float outX, float outZ) =>
        MathF.Abs(outX) >= MathF.Abs(outZ) ? (outX > 0f ? 1 : 3) : (outZ > 0f ? 2 : 0);
}
