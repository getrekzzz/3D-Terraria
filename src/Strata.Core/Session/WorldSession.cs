using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Diagnostics;
using Strata.Core.Lighting;
using Strata.Core.Meshing;
using Strata.Core.Persistence;
using Strata.Core.Simulation;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;
using Strata.Core.World;
using Strata.Core.WorldGen;

namespace Strata.Core.Session;

/// <summary>
/// A live world: content, generator, loaded voxels, tile objects, sky map, clock and persistence.
/// Engine-agnostic; the Godot layer only renders it and feeds input.
/// </summary>
public sealed partial class WorldSession
{
    private WorldSession(ContentDatabase content, WorldMeta meta, WorldSaveStore? store, RenderTables render, ILog log)
    {
        Content = content;
        Meta = meta;
        Store = store;
        Render = render;
        Log = log;
        Settings = meta.ToSettings();
        Ids = meta.CreateIdTables();
        Generator = new WorldGenerator(content, Settings, log);
        World = new VoxelWorld(content, Settings.Dimensions, content.BlockId("abyssal_rock"));
        Clock = new GameClock(meta.WorldTick);
        var plan = Generator.Plan;
        Sky = new SkyMap(World, render,
            (x, z) => (int)MathF.Ceiling(Generator.Terrain.SurfaceHeight(x, z)) + 1,
            (cx, cz) => cx >= 0 && cz >= 0 && cx < World.ChunkCounts.X && cz < World.ChunkCounts.Z
                ? (plan.ColumnTop[plan.ColumnIndex(cx, cz)] - 1) >> ChunkMath.Shift
                : plan.Bands.SeaLevel >> ChunkMath.Shift);
        Flags = new HashSet<string>(meta.Flags, StringComparer.Ordinal);
        Mutations = new WorldGen.Mutations.MutationLog(Generator, content, Settings);
        Mutations.Load(meta, log);
        Events = new HashSet<string>(StringComparer.Ordinal);
        World.TileObjects.Added += o => MarkModified(o);
        World.TileObjects.Removed += o => MarkModified(o);

        if (meta.PlanHash.Length > 0 && meta.PlanHash != plan.Hash)
            log.Warn("session", $"World plan hash changed ({meta.PlanHash} -> {plan.Hash}); unexplored regions will differ from the original generator.");
    }

    public ContentDatabase Content { get; }
    public WorldMeta Meta { get; }
    public WorldSettings Settings { get; }
    public WorldSaveStore? Store { get; }
    public WorldIdTables Ids { get; }
    public RenderTables Render { get; }
    public ILog Log { get; }
    public WorldGenerator Generator { get; }
    public VoxelWorld World { get; }
    public SkyMap Sky { get; }
    public GameClock Clock { get; }
    public HashSet<string> Flags { get; }

    /// <summary>World mutations so far (hardmode ores, wedges); chunks behind the level catch up as they load.</summary>
    public WorldGen.Mutations.MutationLog Mutations { get; }

    /// <summary>Ids of running world events (maintained by the event director).</summary>
    public HashSet<string> Events { get; }

    /// <summary>True while tile objects are being installed from chunk data (not a gameplay change).</summary>
    private bool _installing;

    public static WorldSession Create(ContentDatabase content, WorldSettings settings, string? saveDirectory, RenderTables render, ILog log)
    {
        var meta = new WorldMeta
        {
            Name = settings.Name,
            Seed = settings.Seed,
            Size = settings.Size,
            Difficulty = settings.Difficulty,
            Evil = settings.Evil,
            GeneratorVersion = GenVersion.Current,
            CreatedUtc = DateTime.UtcNow,
            LastPlayedUtc = DateTime.UtcNow,
            WorldTick = GameClock.TicksPerHour * 8L,
        };
        var session = new WorldSession(content, meta, saveDirectory == null ? null : new WorldSaveStore(saveDirectory), render, log);
        var spawn = session.Generator.Plan.Spawn;
        meta.Spawn = new[] { spawn.X, spawn.Y, spawn.Z };
        meta.PlanHash = session.Generator.Plan.Hash;
        session.SaveMetaOnly();
        return session;
    }

    public static WorldSession Load(ContentDatabase content, string saveDirectory, RenderTables render, ILog log)
    {
        var store = new WorldSaveStore(saveDirectory);
        var meta = store.LoadMeta();
        return new WorldSession(content, meta, store, render, log);
    }

    public Int3 SpawnCell => new(Meta.Spawn[0], Meta.Spawn[1], Meta.Spawn[2]);

    /// <summary>Loads or generates a chunk. Thread-safe; runs on workers.</summary>
    public ChunkProduct Produce(Int3 coord)
    {
        var blob = Store?.LoadChunk(coord);
        if (blob != null)
        {
            try
            {
                var decoded = ChunkCodec.Decode(blob, Content, Ids);
                var steps = Mutations.Steps;
                return new ChunkProduct(coord, WorldGen.Mutations.MutationLog.Apply(decoded.Data, coord, steps, decoded.MutationLevel),
                    decoded.TileObjects, Math.Max(decoded.MutationLevel, steps.Count), FromSave: true);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
            {
                Log.Error("session", $"Corrupt saved chunk {coord}; regenerating", ex);
            }
        }
        var generated = Generator.Generate(coord);
        var snapshot = Mutations.Steps; // freshly generated chunks start at level 0
        return new ChunkProduct(coord, WorldGen.Mutations.MutationLog.Apply(generated.Data, coord, snapshot, 0), generated.Objects, snapshot.Count, FromSave: false);
    }

    /// <summary>
    /// Raised on the main thread after a chunk joins the live world. The flag says whether it came from the save
    /// (it may hold liquid that was still moving) rather than fresh from the generator (settled by pass 20).
    /// </summary>
    public event Action<Chunk, bool>? ChunkInstalled;

    /// <summary>Adds a produced chunk to the live world (main thread). Returns sky-exposure changes.</summary>
    public List<(int X, int Z, int Old, int New)> Install(ChunkProduct product)
    {
        var chunk = new Chunk(product.Coord, product.Data) { MutationLevel = product.MutationLevel, FromSave = product.FromSave };
        World.AddChunk(chunk);
        _installing = true;
        try
        {
            foreach (var obj in product.Objects) World.TileObjects.TryAdd(obj);
        }
        finally
        {
            _installing = false;
        }
        var sky = Sky.Rebuild(product.Coord.X, product.Coord.Z);
        ChunkInstalled?.Invoke(chunk, product.FromSave);
        return sky;
    }

    /// <summary>Removes a chunk from memory, returning its encoded blob when it must be saved.</summary>
    public byte[]? Unload(Int3 coord)
    {
        byte[]? blob = null;
        var chunk = World.GetChunk(coord);
        if (chunk != null && chunk.Modified) blob = Encode(chunk);
        _installing = true;
        try
        {
            World.TileObjects.DetachChunk(coord);
        }
        finally
        {
            _installing = false;
        }
        World.RemoveChunk(coord, out _);
        return blob;
    }

    public byte[] Encode(Chunk chunk) =>
        ChunkCodec.Encode(chunk.Capture(), chunk.MutationLevel, World.TileObjects.InChunk(chunk.Coord), Content, Ids);

    private void MarkModified(TileObjectInstance obj)
    {
        if (_installing) return;
        var chunk = World.GetChunk(ChunkMath.CellToChunk(obj.Anchor));
        if (chunk != null) chunk.Modified = true;
    }

    /// <summary>Light sources (tile objects) near a chunk, captured on the main thread for a mesh job.</summary>
    public IReadOnlyList<LightSource> LightSourcesAround(Int3 chunk)
    {
        var box = ChunkMath.ChunkBounds(chunk).Expand(Neighborhood.Margin);
        var list = new List<LightSource>();
        foreach (var obj in World.TileObjects.InBox(box))
        {
            if (obj.Def.Light is not { Length: 3 } l || (l[0] | l[1] | l[2]) == 0) continue;
            var fp = obj.Footprint;
            var cell = obj.Anchor + new Int3(fp.X / 2, Math.Max(0, fp.Y - 1), fp.Z / 2);
            list.Add(new LightSource(cell, LightValue.Pack(0, l[0], l[1], l[2])));
        }
        return list;
    }
}
