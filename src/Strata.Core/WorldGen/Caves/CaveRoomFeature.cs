using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Caves;

/// <summary>What shapes and dresses a planned cave room.</summary>
public enum CaveRoomKind
{
    /// <summary>A long chamber with a low vaulted roof (the underground).</summary>
    Hall,

    /// <summary>A very long, narrow chamber.</summary>
    Gallery,

    /// <summary>A mossy, glowing hollow round a small pool.</summary>
    Grotto,

    /// <summary>A tall round chamber with a great stalagmite in the middle.</summary>
    Dome,

    /// <summary>A wide chamber round an underground lake, with a dry shore to walk.</summary>
    Lake,

    /// <summary>A bowl stepping down in rings, like an amphitheatre.</summary>
    Terraces,

    /// <summary>A chamber lined with glowing crystal.</summary>
    Crystal,

    /// <summary>A vast flat-roofed hall held up by stone columns.</summary>
    Pillars,

    /// <summary>A hot chamber over the underworld: obsidian, ash and lava pools.</summary>
    Magma,

    /// <summary>A small chamber where a switchback turns.</summary>
    Landing,

    /// <summary>A small chamber at the end of a dead-end spur, holding a chest.</summary>
    Niche,

    /// <summary>A side pocket off a large room's wall.</summary>
    Alcove,
}

/// <summary>A liquid pool sunk into a room's floor: a bowl <see cref="Depth"/> rows deep at its centre, filled level with the floor.</summary>
public readonly record struct CavePool(float X, float Z, float Radius, int Depth, LiquidType Liquid);

public enum FormationShape
{
    Stalagmite,
    Stalactite,

    /// <summary>A stalactite grown down onto its stalagmite: floor to roof, narrowest in the middle.</summary>
    Column,
}

/// <summary>A rock (or crystal) formation inside a room, of block <see cref="Block"/>.</summary>
public readonly record struct CaveFormation(float X, float Z, float Radius, float Height, FormationShape Shape, string Block);

/// <summary>An object the treasure pass stands on a room's floor (a chest with its loot table, a pot).</summary>
public readonly record struct CaveTreasure(Int3 Anchor, string Object, string? Loot, int Rotation, int Variant = 0);

/// <summary>
/// A planned room of the cave network: a flat-floored chamber with a rotated elliptical footprint and a vaulted roof. Its
/// floor is the cell row <see cref="FloorY"/> (that row and everything up to the roof is open; the row below stays rock),
/// so passages meet it level and its chests stand on known ground. Pools and terraces sink parts of the floor by up to
/// <see cref="Sink"/> rows. Rooms are pure plan data: <see cref="CaveCarver"/> carves and dresses them chunk by chunk.
/// </summary>
public sealed class CaveRoomFeature : WorldFeature
{
    private readonly float _cos, _sin;

    public CaveRoomFeature(float x, float z, int floorY, float radiusX, float radiusZ, float height, float yawTurns, int sink)
    {
        X = x;
        Z = z;
        FloorY = floorY;
        RadiusX = radiusX;
        RadiusZ = radiusZ;
        Height = height;
        Yaw = yawTurns;
        Sink = sink;
        _cos = Trig.Cos(yawTurns);
        _sin = Trig.Sin(yawTurns);
        float r = MathF.Max(radiusX, radiusZ) * (1f + CaveTuning.RoomWarp) + CaveTuning.ShellCells + 2f;
        int top = floorY + (int)MathF.Ceiling(height * (1f + CaveTuning.CeilingWarp) + CaveTuning.ShellCells) + 2;
        Bounds = new Aabb3i(new Int3((int)MathF.Floor(x - r), floorY - sink - CaveTuning.RoomFloorRock - 1, (int)MathF.Floor(z - r)),
            new Int3((int)MathF.Ceiling(x + r) + 1, top, (int)MathF.Ceiling(z + r) + 1));
    }

    public float X { get; }
    public float Z { get; }
    public int FloorY { get; }
    public float RadiusX { get; }
    public float RadiusZ { get; }

    /// <summary>Height of the roof over the floor at the room's centre.</summary>
    public float Height { get; }

    /// <summary>Rotation of the footprint's long axis (turns).</summary>
    public float Yaw { get; }

    /// <summary>Most rows the floor sinks anywhere (pool bowls, terraces).</summary>
    public int Sink { get; }

    public required CaveRoomKind Room { get; init; }

    /// <summary>Network level, 0 (the underground) to <see cref="CaveTuning.Levels"/> − 1 (the abyss).</summary>
    public int Level { get; init; }

    /// <summary>Roof flatness: 2 = an elliptical vault, 3 = flatter, 4 = a nearly flat roof over steep walls.</summary>
    public int Roof { get; init; } = 2;

    public Vector3 Center => new(X, FloorY, Z);

    /// <summary>Floor points where passages enter.</summary>
    public List<Vector3> Doors { get; } = new();

    public List<CavePool> Pools { get; } = new();
    public List<CaveFormation> Formations { get; } = new();
    public List<CaveTreasure> Treasure { get; } = new();

    /// <summary>Normalized footprint distance of a column: 0 at the centre, 1 on the wall's ellipse.</summary>
    public float Footprint(float x, float z)
    {
        float dx = x - X, dz = z - Z;
        float lx = (dx * _cos + dz * _sin) / RadiusX, lz = (dz * _cos - dx * _sin) / RadiusZ;
        return MathF.Sqrt(lx * lx + lz * lz);
    }

    /// <summary>The footprint's radius (cells) along a horizontal unit direction.</summary>
    public float RadiusToward(float dx, float dz)
    {
        float lx = (dx * _cos + dz * _sin) / RadiusX, lz = (dz * _cos - dx * _sin) / RadiusZ;
        float k = MathF.Sqrt(lx * lx + lz * lz);
        return k > 1e-6f ? 1f / k : MathF.Max(RadiusX, RadiusZ);
    }

    /// <summary>The column at footprint distance <paramref name="u"/> in direction <paramref name="turn"/> (turns, in the room's own frame).</summary>
    public (float X, float Z) At(float u, float turn)
    {
        float a = u * Trig.Cos(turn) * RadiusX, b = u * Trig.Sin(turn) * RadiusZ;
        return (X + a * _cos - b * _sin, Z + a * _sin + b * _cos);
    }

    /// <summary>The column at footprint distance <paramref name="u"/> along a horizontal unit direction (world frame).</summary>
    public (float X, float Z) Along(float dx, float dz, float u)
    {
        float r = RadiusToward(dx, dz) * u;
        return (X + dx * r, Z + dz * r);
    }

    /// <summary>Height of the roof above the floor at footprint distance <paramref name="u"/> (0 at and beyond the wall).</summary>
    public float CeilingAt(float u)
    {
        if (u >= 1f) return 0f;
        float u2 = u * u;
        float up = Roof switch { 3 => u2 * u, 4 => u2 * u2, _ => u2 };
        return Height * MathF.Sqrt(1f - up);
    }

    /// <summary>How far the floor curves up into the wall at footprint distance <paramref name="u"/> (a fillet near the wall).</summary>
    public float RiseAt(float u) => u <= 0.8f ? 0f : MathF.Min(Height, 16f) * 0.32f * DetMath.Square((u - 0.8f) / 0.2f);

    /// <summary>Rows the floor sinks at a column (pools' bowls, the terraces' rings).</summary>
    public int SinkAt(float x, float z, float u)
    {
        int sink = 0;
        if (Room == CaveRoomKind.Terraces && u < 0.6f) sink = u < 0.26f ? 9 : u < 0.43f ? 6 : 3;
        foreach (var p in Pools)
        {
            float dx = (x - p.X) / p.Radius, dz = (z - p.Z) / p.Radius;
            float d2 = dx * dx + dz * dz;
            if (d2 < 1f) sink = Math.Max(sink, (int)MathF.Ceiling(p.Depth * (1f - d2)));
        }
        return Math.Min(sink, Sink);
    }

    /// <summary>The pool a column lies in, if any.</summary>
    public bool InPool(float x, float z, out LiquidType liquid)
    {
        foreach (var p in Pools)
        {
            float dx = x - p.X, dz = z - p.Z;
            if (dx * dx + dz * dz >= p.Radius * p.Radius) continue;
            liquid = p.Liquid;
            return true;
        }
        liquid = LiquidType.None;
        return false;
    }
}
