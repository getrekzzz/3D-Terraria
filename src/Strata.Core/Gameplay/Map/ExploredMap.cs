using System.IO.Compression;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Map;

/// <summary>
/// What one player has discovered of a world, like the reference game's map: a top-down grid of 1 m pixels (2×2 cells).
/// Each pixel remembers the surface seen from outdoors (top block or liquid, and its height) and, separately, what was
/// seen underground (a cave floor or solid rock, and at what depth). Stored sparsely in 64×64-pixel tiles; a pixel value
/// of 0 means unexplored (see <see cref="MapValue"/>).
/// </summary>
public sealed class ExploredMap
{
    public const int CellsPerPixel = 2;
    public const int TileSize = 64;
    private const uint Magic = 0x5041_4D53; // "SMAP"
    private const int FormatVersion = 1;

    private readonly Dictionary<int, MapTile> _tiles = new();

    public ExploredMap(int widthCells, int depthCells)
    {
        Width = (widthCells + CellsPerPixel - 1) / CellsPerPixel;
        Depth = (depthCells + CellsPerPixel - 1) / CellsPerPixel;
    }

    /// <summary>Size in map pixels.</summary>
    public int Width { get; }

    public int Depth { get; }

    public IReadOnlyDictionary<int, MapTile> Tiles => _tiles;

    /// <summary>Changed since it was loaded or last queued for saving (set by every pixel write).</summary>
    public bool Dirty { get; set; }

    /// <summary>A deep copy for saving on another thread (the live map keeps changing on the main thread).</summary>
    public ExploredMap Snapshot()
    {
        var copy = new ExploredMap(Width * CellsPerPixel, Depth * CellsPerPixel);
        foreach (var (key, t) in _tiles)
        {
            var c = new MapTile(t.X, t.Z) { Version = t.Version };
            t.Surface.CopyTo(c.Surface, 0);
            t.SurfaceY.CopyTo(c.SurfaceY, 0);
            t.Cave.CopyTo(c.Cave, 0);
            t.CaveY.CopyTo(c.CaveY, 0);
            copy._tiles[key] = c;
        }
        return copy;
    }

    public static int TileKey(int tx, int tz) => tz << 16 | tx;

    public MapTile? TileAt(int tx, int tz) => _tiles.GetValueOrDefault(TileKey(tx, tz));

    public ushort Surface(int px, int pz) => Get(px, pz, out int i) is { } t ? t.Surface[i] : MapValue.Unexplored;

    public ushort Cave(int px, int pz) => Get(px, pz, out int i) is { } t ? t.Cave[i] : MapValue.Unexplored;

    /// <summary>Records the surface seen at a pixel; returns whether anything changed.</summary>
    public bool SetSurface(int px, int pz, ushort value, short height)
    {
        if (Tile(px, pz, out int i) is not { } t || (t.Surface[i] == value && t.SurfaceY[i] == height)) return false;
        t.Surface[i] = value;
        t.SurfaceY[i] = height;
        t.Version++;
        Dirty = true;
        return true;
    }

    /// <summary>Records what was seen underground at a pixel (floor or rock) and its depth; returns whether anything changed.</summary>
    public bool SetCave(int px, int pz, ushort value, short y)
    {
        if (Tile(px, pz, out int i) is not { } t || (t.Cave[i] == value && t.CaveY[i] == y)) return false;
        t.Cave[i] = value;
        t.CaveY[i] = y;
        t.Version++;
        Dirty = true;
        return true;
    }

    /// <summary>Pixels with anything discovered (surface or underground).</summary>
    public int CountExplored() => _tiles.Values.Sum(t => t.Surface.Zip(t.Cave).Count(v => v.First != 0 || v.Second != 0));

    private MapTile? Get(int px, int pz, out int index)
    {
        index = (pz & (TileSize - 1)) * TileSize + (px & (TileSize - 1));
        return px < 0 || pz < 0 || px >= Width || pz >= Depth ? null : TileAt(px / TileSize, pz / TileSize);
    }

    private MapTile? Tile(int px, int pz, out int index)
    {
        index = (pz & (TileSize - 1)) * TileSize + (px & (TileSize - 1));
        if (px < 0 || pz < 0 || px >= Width || pz >= Depth) return null;
        int key = TileKey(px / TileSize, pz / TileSize);
        if (!_tiles.TryGetValue(key, out var tile)) _tiles[key] = tile = new MapTile(px / TileSize, pz / TileSize);
        return tile;
    }

    /// <summary>Compressed bytes; block ids are written by name so a later build with other ids still reads them.</summary>
    public byte[] Save(Func<int, string> blockName)
    {
        var ids = new SortedSet<int>();
        foreach (var t in _tiles.Values)
            for (int i = 0; i < t.Surface.Length; i++)
            {
                if (MapValue.IsBlockish(t.Surface[i])) ids.Add(MapValue.BlockId(t.Surface[i]));
                if (MapValue.IsBlockish(t.Cave[i])) ids.Add(MapValue.BlockId(t.Cave[i]));
            }
        using var buffer = new MemoryStream();
        using (var z = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(z))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(Width);
            w.Write(Depth);
            w.Write(ids.Count);
            foreach (int id in ids)
            {
                w.Write(id);
                w.Write(blockName(id));
            }
            w.Write(_tiles.Count);
            foreach (var t in _tiles.Values)
            {
                w.Write(t.X);
                w.Write(t.Z);
                for (int i = 0; i < t.Surface.Length; i++)
                {
                    w.Write(t.Surface[i]);
                    w.Write(t.SurfaceY[i]);
                    w.Write(t.Cave[i]);
                    w.Write(t.CaveY[i]);
                }
            }
        }
        return buffer.ToArray();
    }

    /// <summary>Reads <see cref="Save"/> output; blocks whose name no longer resolves become unexplored.</summary>
    public static ExploredMap Load(byte[] data, int widthCells, int depthCells, Func<string, int?> blockId)
    {
        var map = new ExploredMap(widthCells, depthCells);
        using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var r = new BinaryReader(z);
        if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion) throw new InvalidDataException("not a map file");
        int width = r.ReadInt32(), depth = r.ReadInt32();
        if (width != map.Width || depth != map.Depth) throw new InvalidDataException("map size does not match the world");
        var remap = new Dictionary<int, int?>();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32();
            remap[id] = blockId(r.ReadString());
        }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var t = new MapTile(r.ReadInt32(), r.ReadInt32());
            for (int i = 0; i < t.Surface.Length; i++)
            {
                t.Surface[i] = Remap(r.ReadUInt16(), remap);
                t.SurfaceY[i] = r.ReadInt16();
                t.Cave[i] = Remap(r.ReadUInt16(), remap);
                t.CaveY[i] = r.ReadInt16();
            }
            map._tiles[TileKey(t.X, t.Z)] = t;
        }
        return map;
    }

    private static ushort Remap(ushort v, Dictionary<int, int?> remap)
    {
        if (!MapValue.IsBlockish(v)) return v;
        return remap.GetValueOrDefault(MapValue.BlockId(v)) is { } id ? (ushort)((v & MapValue.WallFlag) | (id + 1)) : MapValue.Unexplored;
    }
}

/// <summary>One 64×64 block of map pixels (see <see cref="ExploredMap"/>).</summary>
public sealed class MapTile
{
    public MapTile(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }
    public int Z { get; }
    public readonly ushort[] Surface = new ushort[ExploredMap.TileSize * ExploredMap.TileSize];
    public readonly short[] SurfaceY = new short[ExploredMap.TileSize * ExploredMap.TileSize];
    public readonly ushort[] Cave = new ushort[ExploredMap.TileSize * ExploredMap.TileSize];
    public readonly short[] CaveY = new short[ExploredMap.TileSize * ExploredMap.TileSize];

    /// <summary>Bumped on every change, so a renderer knows to rebuild this tile's picture.</summary>
    public int Version;
}

/// <summary>
/// Map pixel values: 0 unexplored; a block (runtime id + 1); a block seen as solid rock from a cave (<see cref="WallFlag"/>);
/// or a liquid (<see cref="LiquidFlag"/> | liquid type).
/// </summary>
public static class MapValue
{
    public const ushort Unexplored = 0;
    public const ushort WallFlag = 0x4000;
    public const ushort LiquidFlag = 0x8000;

    public static ushort Block(int id) => (ushort)(id + 1);
    public static ushort Wall(int id) => (ushort)(WallFlag | (id + 1));
    public static ushort Liquid(LiquidType type) => (ushort)(LiquidFlag | (int)type);
    public static bool IsLiquid(ushort v) => (v & LiquidFlag) != 0;
    public static bool IsWall(ushort v) => !IsLiquid(v) && (v & WallFlag) != 0;
    public static bool IsBlockish(ushort v) => v != Unexplored && !IsLiquid(v);
    public static int BlockId(ushort v) => (v & 0x3FFF) - 1;
    public static LiquidType LiquidOf(ushort v) => (LiquidType)(v & 0xFF);
}
