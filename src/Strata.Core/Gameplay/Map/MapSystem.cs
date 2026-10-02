using Strata.Core.Common;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Map;

/// <summary>
/// Reveals the map as each player explores: out under the open sky the land around them (its top blocks, water and
/// lava, and their heights), underground the caves around them at their own depth (floors, and the rock walls between
/// them). Maps are saved per player per world, as in the reference game (<c>maps/&lt;player&gt;.map</c> in the world folder).
/// A reveal that met ground not streamed in yet (a fresh world's spawn, a teleport) is tried again while the player
/// stands still, at a backing-off pace, so the map fills in around them without their having to walk.
/// </summary>
public sealed class MapSystem
{
    /// <summary>Ticks between reveals (a player also has to move a little before the next one).</summary>
    public const int RevealInterval = 6;

    /// <summary>Reveal radii in cells: wide outdoors, a torch's reach underground.</summary>
    public const int SurfaceRadius = 64, CaveRadius = 26;

    /// <summary>Underground, floors are looked for this far above and below the player's feet (cells).</summary>
    public const int CaveUp = 6, CaveDown = 14;

    /// <summary>
    /// Ticks before an incomplete reveal (some columns were not loaded) is tried again in place; each further try waits
    /// twice as long, up to <see cref="MaxRetryInterval"/>.
    /// </summary>
    public const int RetryInterval = 30, MaxRetryInterval = 240;

    public const string MapDirectory = "maps";

    private readonly GameWorld _world;
    private readonly Dictionary<PlayerEntity, Entry> _maps = new();
    private readonly ushort _air;

    public MapSystem(GameWorld world)
    {
        _world = world;
        _air = (ushort)world.Session.Content.BlockId("air");
    }

    /// <summary>The player's map of this world (loaded from disk the first time).</summary>
    public ExploredMap For(PlayerEntity player)
    {
        if (_maps.TryGetValue(player, out var entry)) return entry.Map;
        var map = LoadOrNew(player);
        _maps[player] = new Entry(map);
        return map;
    }

    public void Tick()
    {
        if (_world.Session.Clock.Tick % RevealInterval != 0) return;
        foreach (var p in _world.Players)
            if (p.Alive) Reveal(p);
    }

    /// <summary>
    /// Reveals around a player if they moved since the last reveal, or the last one was incomplete and its retry is due
    /// (or always with <paramref name="force"/>).
    /// </summary>
    public void Reveal(PlayerEntity player, bool force = false)
    {
        var map = For(player);
        var e = _maps[player];
        var feet = player.FeetCell;
        long now = _world.Session.Clock.Tick;
        if (!force && e.Last is { } last && Math.Abs(feet.X - last.X) + Math.Abs(feet.Y - last.Y) + Math.Abs(feet.Z - last.Z) < 3
            && (e.RetryAt < 0 || now < e.RetryAt)) return;
        bool moved = e.Last is not { } prev || prev != feet;
        e.Last = feet;
        int missing = IsOutdoors(player) ? RevealSurface(map, feet, SurfaceRadius) : RevealCaves(map, feet, CaveRadius);
        if (missing == 0)
        {
            e.RetryAt = -1;
            return;
        }
        // Ground not streamed in yet: look again soon, then less and less often while nothing around changes.
        e.RetryWait = moved || e.RetryAt < 0 ? RetryInterval : Math.Min(MaxRetryInterval, e.RetryWait * 2);
        e.RetryAt = now + e.RetryWait;
    }

    /// <summary>Map pixels the player has discovered (the <c>map.revealed_cells</c> probe).</summary>
    public int Explored(PlayerEntity player) => For(player).CountExplored();

    /// <summary>Nothing but sky above the player (the map shows the surface; otherwise the caves at their depth).</summary>
    public bool IsOutdoors(PlayerEntity player)
    {
        var feet = player.FeetCell;
        return feet.Y >= _world.Session.Sky.SkyTop(feet.X, feet.Z) - 2;
    }

    /// <summary>Records the top of every column around <paramref name="center"/>; returns the columns not loaded yet.</summary>
    private int RevealSurface(ExploredMap map, Int3 center, int radius)
    {
        var world = _world.Session.World;
        var sky = _world.Session.Sky;
        return ForPixels(map, center, radius, (px, pz, x, z) =>
        {
            int top = sky.SkyTop(x, z);
            // The first known non-air cell at or just below the sky top (the top block of the column).
            for (int y = top + 1; y >= top - 4; y--)
            {
                ushort b = world.GetBlock(x, y, z);
                if (b == VoxelWorld.Unknown) return false;
                byte liquid = world.GetLiquid(x, y, z);
                if (liquid != 0)
                {
                    map.SetSurface(px, pz, MapValue.Liquid(LiquidCode.Type(liquid)), (short)y);
                    return true;
                }
                if (b == _air) continue;
                map.SetSurface(px, pz, MapValue.Block(b), (short)y);
                return true;
            }
            return true;
        });
    }

    /// <summary>Records the cave floor (or rock) of every column around <paramref name="center"/>; returns the columns not loaded yet.</summary>
    private int RevealCaves(ExploredMap map, Int3 center, int radius)
    {
        var world = _world.Session.World;
        return ForPixels(map, center, radius, (px, pz, x, z) =>
        {
            // The open floor nearest the player's height; solid rock if the column is closed there.
            ushort rock = VoxelWorld.Unknown;
            for (int y = center.Y + CaveUp; y >= center.Y - CaveDown; y--)
            {
                ushort b = world.GetBlock(x, y, z);
                if (b == VoxelWorld.Unknown) return false;
                if (b == _air || world.GetLiquid(x, y, z) != 0)
                {
                    ushort below = world.GetBlock(x, y - 1, z);
                    if (below == VoxelWorld.Unknown) return false;
                    if (below == _air) continue;
                    byte liquid = world.GetLiquid(x, y, z);
                    map.SetCave(px, pz, liquid != 0 ? MapValue.Liquid(LiquidCode.Type(liquid)) : MapValue.Block(below), (short)(y - 1));
                    return true;
                }
                if (y == center.Y) rock = b;
            }
            if (rock != VoxelWorld.Unknown) map.SetCave(px, pz, MapValue.Wall(rock), (short)center.Y);
            return true;
        });
    }

    /// <summary>
    /// Visits every map pixel within <paramref name="radius"/> cells, sampling the cell at its centre; returns the pixels
    /// whose visit found the column not loaded (it returned false).
    /// </summary>
    private static int ForPixels(ExploredMap map, Int3 center, int radius, Func<int, int, int, int, bool> visit)
    {
        const int s = ExploredMap.CellsPerPixel;
        int pr = radius / s, cx = center.X / s, cz = center.Z / s, missing = 0;
        for (int pz = cz - pr; pz <= cz + pr; pz++)
            for (int px = cx - pr; px <= cx + pr; px++)
            {
                if ((px - cx) * (px - cx) + (pz - cz) * (pz - cz) > pr * pr || px < 0 || pz < 0 || px >= map.Width || pz >= map.Depth) continue;
                if (!visit(px, pz, px * s + s / 2, pz * s + s / 2)) missing++;
            }
        return missing;
    }

    /// <summary>Writes every player's map into the world folder (worlds without a folder keep maps in memory only).</summary>
    public void Save()
    {
        foreach (var (player, entry) in _maps)
        {
            if (PathFor(player) is not { } path) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var blocks = _world.Session.Content.Blocks;
            Persistence.AtomicFile.WriteAllBytes(path, entry.Map.Save(id => blocks[id].Id));
        }
    }

    /// <summary>
    /// Adds every changed map to a save batch (main thread): a deep copy is taken now and compressed on the save thread.
    /// </summary>
    public void QueueSave(Persistence.SaveBatch batch)
    {
        var blocks = _world.Session.Content.Blocks;
        foreach (var (player, entry) in _maps)
        {
            if (!entry.Map.Dirty || PathFor(player) is not { } path) continue;
            var map = entry.Map;
            var snapshot = map.Snapshot();
            map.Dirty = false;
            // A failed write marks the map changed again (on the main thread), so the next save retries it.
            batch.Files.Add(new Persistence.SaveFile(path, () => snapshot.Save(id => blocks[id].Id), () => map.Dirty = true));
        }
    }

    /// <summary>A player's map and where and when it was last revealed.</summary>
    private sealed class Entry(ExploredMap map)
    {
        public ExploredMap Map { get; } = map;

        /// <summary>The feet cell of the last reveal (null before the first).</summary>
        public Int3? Last { get; set; }

        /// <summary>The tick an incomplete reveal is tried again, or -1 when the last one was complete.</summary>
        public long RetryAt { get; set; } = -1;

        public int RetryWait { get; set; }
    }

    private ExploredMap LoadOrNew(PlayerEntity player)
    {
        var size = _world.Session.Settings.Dimensions;
        if (PathFor(player) is { } path && File.Exists(path))
        {
            try
            {
                var content = _world.Session.Content;
                return ExploredMap.Load(File.ReadAllBytes(path), size.X, size.Z, name => content.Blocks.TryGet(name, out var b) ? b.RuntimeId : null);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException)
            {
                _world.Session.Log.Write(Diagnostics.LogLevel.Warning, "map", $"Could not read {path} ({ex.Message}); starting a fresh map.");
            }
        }
        return new ExploredMap(size.X, size.Z);
    }

    private string? PathFor(PlayerEntity player)
    {
        if (_world.Session.Store is not { } store) return null;
        var name = string.Concat(player.State.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(store.Directory, MapDirectory, $"{name}.map");
    }
}
