using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Caves;

/// <summary>Room dressing: wall materials by room kind, and the formations.</summary>
internal sealed partial class CaveCarver
{
    private enum Face
    {
        Wall,
        Floor,
        Ceiling,
    }

    private static readonly string[] Gems = { "amethyst_ore", "topaz_ore", "sapphire_ore", "emerald_ore", "ruby_ore", "diamond_ore" };

    /// <summary>
    /// Dresses the rock bounding each room, in wobbly patches: moss carpets and glowing argon moss in grottos, glowing
    /// patches on the roofs of the great chambers (so their size shows in the dark), crystal and gems lining the crystal
    /// caves, obsidian, ash and hellstone round magma chambers with obsidian rims round the lava, a glow round treasure
    /// niches and silt under the pools. Only plain stone, deepstone and ash take a dressing, so biome rock keeps its look.
    /// </summary>
    private void DressWalls()
    {
        var p = _ctx.Palette;
        var blocks = _ctx.Blocks;
        const int layer = Wide * Wide;
        int n = ChunkGenContext.Size;
        for (int ly = 0; ly < n; ly++)
            for (int lz = 0; lz < n; lz++)
                for (int lx = 0; lx < n; lx++)
                {
                    int i = ChunkGenContext.Index(lx, ly, lz);
                    ushort b = blocks[i];
                    if (b != p.Stone && b != p.Deepstone && b != p.Ash) continue;
                    int w = ((ly + 1) * Wide + lz + 1) * Wide + lx + 1;
                    byte above = Owner[w + layer], below = Owner[w - layer];
                    byte any = (byte)(above | below | Owner[w - 1] | Owner[w + 1] | Owner[w - Wide] | Owner[w + Wide]);
                    if (any == 0) continue;
                    int x = _ctx.Origin.X + lx, y = _ctx.Origin.Y + ly, z = _ctx.Origin.Z + lz;
                    if ((any & LavaOwner) != 0)
                    {
                        blocks[i] = p.Obsidian;
                        continue;
                    }
                    byte o;
                    Face face;
                    if ((above & RoomBits) != 0) (o, face) = (above, Face.Floor);
                    else if ((below & RoomBits) != 0) (o, face) = (below, Face.Ceiling);
                    else
                    {
                        o = (Owner[w - 1] & RoomBits) != 0 ? Owner[w - 1] : (Owner[w + 1] & RoomBits) != 0 ? Owner[w + 1]
                            : (Owner[w - Wide] & RoomBits) != 0 ? Owner[w - Wide] : Owner[w + Wide];
                        face = Face.Wall;
                    }
                    int id = (o & RoomBits) - 1;
                    if (id < 0 || id >= _rooms.Count) continue;
                    blocks[i] = Dress(_rooms[id], face, x, y, z, b, (above & WaterOwner) != 0);
                }
    }

    private ushort Dress(CaveRoomFeature room, Face face, int x, int y, int z, ushort current, bool underWater)
    {
        var p = _ctx.Palette;
        int j = (int)MathF.Floor(Warp(x, y, z) * 3f);
        float patch = Hash.ToUnitFloat(Hash.Cell(room.Seed, (x + j) / 3, (y - j) / 3, (z + j) / 3));
        float fine = Hash.ToUnitFloat(Hash.Cell(room.Seed ^ 0x5A5AUL, x, y, z));
        ushort moss = p.MossStone, glow = p.GlowMossStone;
        if (underWater) return room.Room is CaveRoomKind.Lake or CaveRoomKind.Grotto ? p.Silt : current;
        switch (room.Room)
        {
            case CaveRoomKind.Grotto:
                return face switch
                {
                    Face.Floor => patch < 0.65f ? moss : current,
                    Face.Ceiling => patch < 0.38f ? glow : patch < 0.6f ? moss : current,
                    _ => patch < 0.5f ? moss : patch < 0.6f ? glow : current,
                };
            case CaveRoomKind.Lake:
                return face == Face.Ceiling ? (patch < 0.2f ? glow : current) : face == Face.Wall && patch < 0.18f ? moss : current;
            case CaveRoomKind.Dome:
                return face == Face.Ceiling ? (patch < 0.18f ? glow : current) : face == Face.Wall && patch < 0.12f ? moss : current;
            case CaveRoomKind.Terraces:
            {
                // The bowl's bottom glows, so the rings show from its rim.
                if (face != Face.Floor) return face == Face.Ceiling && patch < 0.15f ? glow : current;
                bool bottom = room.SinkAt(x + 0.5f, z + 0.5f, room.Footprint(x + 0.5f, z + 0.5f)) >= 6;
                return bottom && patch < 0.35f ? glow : patch < 0.45f ? moss : current;
            }
            case CaveRoomKind.Pillars:
                return face == Face.Ceiling && patch < 0.14f ? glow : current;
            case CaveRoomKind.Crystal:
            {
                if (face == Face.Floor) return patch < 0.22f ? p.Crystal : current;
                if (patch < 0.45f) return p.Crystal;
                return fine < 0.07f ? p.Id(Gems[(int)(room.Seed % (ulong)Gems.Length)]) : current;
            }
            case CaveRoomKind.Magma:
                return face switch
                {
                    Face.Floor => patch < 0.45f ? p.Ash : patch < 0.62f ? p.Obsidian : current,
                    Face.Ceiling => patch < 0.25f ? p.Obsidian : fine < 0.03f ? p.Cinderstone : current,
                    _ => patch < 0.35f ? p.Obsidian : fine < 0.04f ? p.Cinderstone : current,
                };
            case CaveRoomKind.Hall:
            case CaveRoomKind.Gallery:
                return face == Face.Ceiling ? (patch < 0.07f ? glow : current) : face == Face.Wall && patch < 0.15f ? moss : current;
            case CaveRoomKind.Landing:
                return face == Face.Ceiling && patch < 0.25f ? glow : current;
            case CaveRoomKind.Niche:
                return face != Face.Floor && patch < 0.45f ? glow : current;
            case CaveRoomKind.Alcove:
                return face == Face.Wall && patch < 0.3f ? moss : current;
            default:
                return current;
        }
    }

    /// <summary>Grows a formation into the open cells of its room: a stalagmite tapering up from the floor, a stalactite
    /// tapering down from the roof (rooted past the roof's warp), or a column from floor to roof, narrowest halfway.</summary>
    private void Grow(CaveRoomFeature room, CaveFormation f)
    {
        var p = _ctx.Palette;
        ushort block = p.Id(f.Block);
        float u = room.Footprint(f.X, f.Z);
        float roofHeight = room.CeilingAt(u);
        float floor = room.FloorY - room.SinkAt(f.X, f.Z, u);
        float roof = room.FloorY + roofHeight * (1f + CaveTuning.CeilingWarp) + 1f;
        float tip = room.FloorY + roofHeight - f.Height;
        float lo = f.Shape == FormationShape.Stalactite ? tip : floor - 1f;
        float hi = f.Shape == FormationShape.Stalagmite ? floor + f.Height : roof;
        int r = (int)MathF.Ceiling(f.Radius) + 1;
        var box = new Aabb3i(_ctx.Origin, _ctx.Origin + Int3.One * ChunkGenContext.Size).Intersection(new Aabb3i(
            new Int3((int)MathF.Floor(f.X) - r, (int)MathF.Floor(lo), (int)MathF.Floor(f.Z) - r),
            new Int3((int)MathF.Floor(f.X) + r + 1, (int)MathF.Ceiling(hi) + 1, (int)MathF.Floor(f.Z) + r + 1)));
        if (box.IsEmpty) return;
        var blocks = _ctx.Blocks;
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                {
                    int i = _ctx.WorldIndex(x, y, z);
                    if (blocks[i] != p.Air || room.Footprint(x + 0.5f, z + 0.5f) >= 1f) continue;
                    float dx = x + 0.5f - f.X, dz = z + 0.5f - f.Z, cy = y + 0.5f;
                    float radius = f.Shape switch
                    {
                        FormationShape.Stalagmite => Taper(f.Radius, (cy - floor) / f.Height),
                        FormationShape.Stalactite => Taper(f.Radius, (hi - cy) / (hi - tip)),
                        _ => f.Radius * (0.55f + 0.45f * DetMath.Square(2f * DetMath.Clamp01((cy - floor) / (roof - floor)) - 1f)),
                    };
                    if (dx * dx + dz * dz >= radius * radius) continue;
                    blocks[i] = block;
                    if (_ctx.Liquids != null) _ctx.Liquids[i] = 0;
                }
    }

    /// <summary>Radius of a cone with a full base at t = 0 (and below) narrowing to a point at t = 1.</summary>
    private static float Taper(float radius, float t)
    {
        if (t >= 1f) return 0f;
        if (t <= 0f) return radius;
        float s = 1f - t;
        return radius * s * (0.6f + 0.4f * s);
    }
}
