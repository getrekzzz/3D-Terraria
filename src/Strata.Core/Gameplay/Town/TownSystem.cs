using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Diagnostics;
using Strata.Core.Gameplay.Housing;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Persistence;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// The town: residents move in when their data-driven arrival conditions hold and a vacant valid home exists, keep
/// their homes while the rooms stay valid, stroll by day and walk home at night (voxel A*, opening doors), and are
/// saved with the world. Talking, trading and services live in <see cref="TownCommerce"/>.
/// </summary>
public sealed partial class TownSystem
{
    private readonly GameWorld _world;
    private readonly List<TownNpc> _residents = new();
    private readonly Dictionary<string, ICondition> _arrival = new();
    private long _nextId = 1;
    private long _homeAudit;
    private WorldStatus? _status;

    /// <summary>The Dryad's incremental world purity sampler (created on first use).</summary>
    public WorldStatus Status => _status ??= new WorldStatus(_world.Session);
    private Pcg32 _rng;

    public TownSystem(GameWorld world, TownConfig? config = null)
    {
        _world = world;
        Config = config ?? new TownConfig();
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, 0x544F574EUL));
        foreach (var def in world.Session.Content.TownNpcs.All)
        {
            _arrival[def.Id] = ConditionCompiler.CompileAll(def.Arrival);
            _leaves[def.Id] = ConditionCompiler.CompileAll(def.LeavesWhen);
        }
        Load(world.Session.Meta);
    }

    public TownConfig Config { get; set; }

    /// <summary>Move-ins can be switched off (tests, peaceful building).</summary>
    public bool ArrivalsEnabled { get; set; } = true;

    public IReadOnlyList<TownNpc> Residents => _residents;

    /// <summary>The player's journal for town conditions (bestiary, kills, achievements); set by the journal.</summary>
    public Func<PlayerEntity, IPlayerRecords?>? PlayerRecords { get; set; }

    private NavAgent Agent => NavAgent.Humanoid;
    private VoxelWorld World => _world.Session.World;

    /// <summary>A resident moved in (HUD announcement).</summary>
    public event Action<TownNpc>? Arrived;

    /// <summary>A resident's home stopped being valid; they are homeless until another is found.</summary>
    public event Action<TownNpc>? HomeLost;

    public event Action<TownNpc>? Spawned;
    public event Action<TownNpc>? Removed;

    public TownNpc? Find(string id) => _residents.FirstOrDefault(r => r.Def.Id == id);

    public void Tick()
    {
        long tick = _world.Session.Clock.Tick;
        if (!_world.Session.Meta.TownStarted) TryPlaceStarters();
        if (ArrivalsEnabled && tick % Config.ArrivalInterval == 0) Arrivals(force: false, budgeted: true);
        if (tick % LandmarkInterval == 0) UpdateLandmarks();
        if (tick % Config.HomeCheckInterval == 0) CheckHomes();
        foreach (var r in _residents) TickResident(r);
        _status?.Tick();
    }

    /// <summary>
    /// Runs the move-in check now (commands, tests). Homeless residents are re-housed at any hour; new residents only
    /// arrive by day unless <paramref name="force"/> is set. Returns the resident that arrived, if any.
    /// </summary>
    public TownNpc? CheckArrivals(bool force = false) => Arrivals(force, budgeted: false);

    private TownNpc? Arrivals(bool force, bool budgeted)
    {
        var player = _world.LocalPlayer;
        if (player == null) return null;
        var rooms = VacantRooms(budgeted);
        // Homeless residents are housed first.
        foreach (var r in _residents.Where(r => r.Home == null && r.CountsAsResident))
        {
            if (rooms.Count == 0) break;
            SetHome(r, rooms[0]);
            rooms.RemoveAt(0);
        }
        if (!force && !_world.Session.Clock.IsDay) return null; // Terraria: nobody new moves in at night
        // Each vacant home is judged by its own surroundings (a mushroom house draws Truffle).
        foreach (var room in rooms)
        {
            var ctx = TownContext(player);
            ctx.Biomes = _world.Session.ContextAt(room.HomeNode!.Value + Int3.Up).Biomes;
            ctx.Random = _rng; // chance conditions (the Party Girl) draw from the town's stream
            foreach (var def in _world.Session.Content.TownNpcs.All)
            {
                if (Find(def.Id) != null || def.Starter || def.Landmark != null) continue;
                bool ok = _arrival[def.Id].Evaluate(ctx) && !HasLeft(def, ctx);
                _rng = ctx.Random;
                if (!ok) continue;
                var npc = Create(def, room.HomeNode!.Value);
                SetHome(npc, room);
                Arrived?.Invoke(npc);
                return npc;
            }
        }
        return null;
    }

    /// <summary>Adds a resident at a node (commands; bypasses arrival conditions). Homeless until a home is found.</summary>
    public TownNpc Summon(TownNpcDef def, Int3 node)
    {
        var existing = Find(def.Id);
        if (existing != null) return existing;
        var npc = Create(def, node);
        return npc;
    }

    public bool Dismiss(TownNpc npc)
    {
        if (!_residents.Remove(npc)) return false;
        Removed?.Invoke(npc);
        return true;
    }

    /// <summary>
    /// Valid rooms nobody lives in, found from chairs in loaded chunks (every home needs one), through the room cache.
    /// <paramref name="budgeted"/> (the background check) scans only a few new rooms per tick; the rest wait.
    /// </summary>
    public List<Room> VacantRooms(bool budgeted = false)
    {
        var taken = new HashSet<Int3>();
        foreach (var r in _residents)
            if (r.HomeRoom is { } k) taken.Add(k);
        var rooms = new List<Room>();
        var chairs = new List<Voxels.TileObjects.TileObjectInstance>();
        foreach (var o in World.TileObjects.All)
            if (Array.IndexOf(o.Def.Housing, "chair") >= 0) chairs.Add(o);
        chairs.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var obj in chairs)
        {
            if (_world.Rooms.Get(obj.Anchor, budgeted) is not { } room) continue; // over budget: next interval
            if (room.Valid && room.HomeNode != null && !taken.Contains(room.Key) && !rooms.Exists(r => r.Key == room.Key)) rooms.Add(room);
        }
        return rooms;
    }

    /// <summary>Condition context for town decisions: the player's surroundings plus wealth, life, items and residents.</summary>
    public ConditionContext TownContext(PlayerEntity player)
    {
        var ctx = _world.Session.ContextAt(player.FeetCell);
        ctx.PlayerCoins = _world.Players.Max(p => Coins.Total(p.State.Inventory));
        ctx.PlayerMaxLife = _world.Players.Max(p => p.State.Vitals.MaxLife);
        FillTownFacts(ctx);
        ctx.ItemCount = key => _world.Players.Sum(p => key.StartsWith('#')
            ? p.State.Inventory.CountAll(d => d.HasTag(key[1..]))
            : key.StartsWith('@')
                ? Enum.TryParse<ItemCategory>(key[1..], ignoreCase: true, out var cat) ? p.State.Inventory.CountAll(d => d.Category == cat) : 0
                : _world.Session.Content.Items.TryGet(key, out var item) ? p.State.Inventory.CountAll(item) : 0);
        ctx.AnglerQuests = 0; // per-player quest counts arrive with the Angler's quest log (P5-23)
        ctx.Records = PlayerRecords?.Invoke(player);
        return ctx;
    }

    /// <summary>Town population facts: who counts as a resident, and how many of them are housed.</summary>
    public void FillTownFacts(ConditionContext ctx)
    {
        var ids = new HashSet<string>();
        int housed = 0;
        foreach (var r in _residents)
        {
            if (!r.CountsAsResident) continue;
            ids.Add(r.Def.Id);
            if (r.IsHoused) housed++;
        }
        ctx.Residents = ids;
        ctx.ResidentCount = housed;
    }

    private void TryPlaceStarters()
    {
        var spawn = _world.Session.SpawnCell;
        if (World.GetBlock(spawn) == VoxelWorld.Unknown || _world.LocalPlayer == null) return;
        foreach (var def in _world.Session.Content.TownNpcs.All.Where(d => d.Starter && d.Landmark == null))
        {
            if (Find(def.Id) != null) continue;
            var node = _world.Navigator.NearestStandable(spawn + new Int3(3, 0, 2), Agent, 6) ?? spawn;
            Create(def, node);
        }
        _world.Session.Meta.TownStarted = true;
    }

    private TownNpc Create(TownNpcDef def, Int3 node)
    {
        var name = def.Names.Length > 0 ? def.Names[_rng.Range(0, def.Names.Length - 1)] : def.DisplayName;
        var npc = new TownNpc(def, name) { Id = _nextId++, Position = NodeFeet(node), IdleTicks = Config.IdleTicksMin };
        _residents.Add(npc);
        Spawned?.Invoke(npc);
        return npc;
    }

    private static void SetHome(TownNpc npc, Room room)
    {
        npc.Home = room.HomeNode;
        npc.HomeRoom = room.Key;
        npc.BannerCell = BannerOf(room);
    }

    /// <summary>
    /// Re-validates homes through the room cache (edits near a home drop its cached room). One home per check is
    /// rescanned regardless, in case an edit path slipped past the invalidation.
    /// </summary>
    private void CheckHomes()
    {
        if (_residents.Count > 0 && _residents[(int)(_homeAudit++ % _residents.Count)].Home is { } audited)
            _world.Rooms.Invalidate(HomeCell(audited));
        foreach (var r in _residents)
        {
            if (r.Home is not { } home) continue;
            var cell = HomeCell(home);
            if (World.GetBlock(cell) == VoxelWorld.Unknown) continue; // cannot judge an unloaded home
            if (_world.Rooms.Get(cell, budgeted: true) is not { } room) continue; // over budget: judged next time
            if (room.Valid && room.Key == r.HomeRoom)
            {
                r.BannerCell ??= BannerOf(room); // not saved: found again once the home is loaded
                continue;
            }
            r.Home = null;
            r.HomeRoom = null;
            r.BannerCell = null;
            HomeLost?.Invoke(r);
        }
    }

    /// <summary>An air cell inside the room of a home node (the node is the corner of four body columns).</summary>
    private static Int3 HomeCell(Int3 home) => home + new Int3(-1, 1, -1);

    /// <summary>Feet position (m) of a navigation node (grid vertex).</summary>
    public static Vector3 NodeFeet(Int3 node) => new Vector3(node.X, node.Y, node.Z) * Units.CellSize;

    // --- Persistence -------------------------------------------------------------------------------------------

    /// <summary>Writes residents into the world metadata (called before saving).</summary>
    public void Persist()
    {
        _world.Session.Meta.Residents = _residents.Select(r => new WorldMeta.ResidentModel(
            r.Def.Id, r.Name,
            r.Home is { } h ? new[] { h.X, h.Y, h.Z } : null,
            r.HomeRoom is { } k ? new[] { k.X, k.Y, k.Z } : null,
            new[] { r.Position.X, r.Position.Y, r.Position.Z }, r.Life, r.RespawnTicks,
            r.Bound, r.DiedAtTick)).ToList();
    }

    private void Load(WorldMeta meta)
    {
        var content = _world.Session.Content;
        foreach (var m in meta.Residents)
        {
            if (!content.TownNpcs.TryGet(m.Id, out var def))
            {
                _world.Session.Log.Warn("town", $"Saved resident '{m.Id}' is not in content; they have left town.");
                continue;
            }
            var npc = new TownNpc(def, m.Name)
            {
                Id = _nextId++,
                Home = m.Home is { Length: 3 } h ? new Int3(h[0], h[1], h[2]) : null,
                HomeRoom = m.HomeRoom is { Length: 3 } k ? new Int3(k[0], k[1], k[2]) : null,
                Position = m.Position is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) : Vector3.Zero,
                Life = Math.Clamp(m.Life, 0, def.Life),
                RespawnTicks = Math.Min(m.RespawnTicks, LandmarkInterval), // old saves counted the whole delay here
                Bound = m.Bound,
                DiedAtTick = m.DiedAt,
                IdleTicks = Config.IdleTicksMin,
            };
            _residents.Add(npc);
        }
    }
}
