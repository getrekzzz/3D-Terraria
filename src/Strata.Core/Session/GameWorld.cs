using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Persistence;
using Strata.Core.Streaming;

namespace Strata.Core.Session;

/// <summary>A player character inside a world: persistent state plus its live body (synced by the engine layer).</summary>
public sealed class PlayerEntity
{
    public PlayerEntity(PlayerState state) => State = state;

    public PlayerState State { get; }

    /// <summary>Feet position in meters.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Velocity { get; set; }

    /// <summary>Normalized aim direction.</summary>
    public Vector3 Aim { get; set; } = -Vector3.UnitZ;

    /// <summary>Ticks until the held item can be used again.</summary>
    public int UseCooldown { get; set; }

    /// <summary>Ticks left in the current use animation (swing/shoot pose).</summary>
    public int UseAnimation { get; set; }

    /// <summary>Crisp shape used when placing blocks.</summary>
    public Voxels.CellShape BuildShape { get; set; } = Voxels.CellShape.Full;

    /// <summary>Quarter turns about +Y for shapes and objects (added to the facing direction).</summary>
    public int BuildRotation { get; set; }

    /// <summary>Precision mode: tools affect a single cell.</summary>
    public bool Precision { get; set; }

    public static readonly Vector3 HalfExtents = new(0.45f, 0.9f, 0.45f);

    /// <summary>Eye position in meters.</summary>
    public Vector3 Eye => Position + new Vector3(0, 1.6f, 0);

    /// <summary>
    /// Horizontal facing as a <see cref="Voxels.ShapeCode"/> rotation: 0 = looking +Z, 1 = −X, 2 = −Z, 3 = +X.
    /// Slopes placed with it rise away from the player; objects face the player.
    /// </summary>
    public int FacingQuarter
    {
        get
        {
            int q = (int)MathF.Round(MathF.Atan2(Aim.X, Aim.Z) / (MathF.PI / 2f));
            return ((-q % 4) + 4) % 4;
        }
    }

    public Vector3 Center => Position + new Vector3(0, HalfExtents.Y, 0);

    public bool Alive => State.Vitals.Life > 0;

    /// <summary>Box in cell units (for placement blocking).</summary>
    public (Vector3 Min, Vector3 Max) CellBox => ((Center - HalfExtents) / Units.CellSize, (Center + HalfExtents) / Units.CellSize);
}

/// <summary>
/// The simulation of one open world, independent of any engine: chunk streaming and jobs, the fixed-tick clock,
/// dropped items, world editing and players. The Godot layer renders it and feeds input; tests and the smoke
/// runner drive it directly.
/// </summary>
public sealed class GameWorld : IDisposable
{
    public const int AutosaveTicks = Units.TicksPerSecond * 120;
    private int _autosaveCounter;

    public GameWorld(WorldSession session, StreamingSettings streaming, int workers, string? playerDirectory = null)
    {
        Session = session;
        Jobs = new JobSystem(Math.Max(1, workers), session.Log);
        Streamer = new ChunkStreamer(session, Jobs, streaming);
        Drops = new DroppedItemSystem();
        Editor = new WorldEditor(session, Drops);
        Uses = new ItemUseTable(session.Content);
        ItemUse = new ItemUseSystem(this);
        Interaction = new ObjectInteraction(this);
        PlayerDirectory = playerDirectory;
        Editor.ChunksDirty += dirty => Streamer.MarkDirty(dirty);
    }

    public WorldSession Session { get; }
    public JobSystem Jobs { get; }
    public ChunkStreamer Streamer { get; }
    public DroppedItemSystem Drops { get; }
    public WorldEditor Editor { get; }
    public ItemUseTable Uses { get; }
    public ItemUseSystem ItemUse { get; }
    public ObjectInteraction Interaction { get; }
    public string? PlayerDirectory { get; }
    public List<PlayerEntity> Players { get; } = new();

    public PlayerEntity? LocalPlayer => Players.Count > 0 ? Players[0] : null;

    /// <summary>Raised after an autosave with the number of chunks written.</summary>
    public event Action<int>? Autosaved;

    /// <summary>Per-frame work: stream around the focus and finish completed jobs within a time budget.</summary>
    public void Update(Int3 focusCell, double jobBudgetMs)
    {
        Streamer.Update(focusCell);
        Jobs.Drain(jobBudgetMs);
    }

    /// <summary>One fixed simulation tick (60 Hz).</summary>
    public void Tick()
    {
        Session.Clock.Advance();
        const float dt = 1f / Units.TicksPerSecond;
        var player = LocalPlayer;
        if (player is { Alive: true })
            Drops.Tick(Session.World, dt, player.Center, stack => player.State.Inventory.Pickup(stack));
        else
            Drops.Tick(Session.World, dt, new Vector3(float.MaxValue), s => s);
        foreach (var p in Players) TickPlayer(p);
        if (Session.Clock.Tick % 600 == 0) Editor.Damage.Prune(Session.Clock.Tick);
        if (++_autosaveCounter >= AutosaveTicks)
        {
            _autosaveCounter = 0;
            Autosaved?.Invoke(SaveAll());
        }
    }

    private void TickPlayer(PlayerEntity p)
    {
        var state = p.State;
        if (p.UseCooldown > 0) p.UseCooldown--;
        if (p.UseAnimation > 0) p.UseAnimation--;
        if (state.Buffs.Tick()) state.Recompute();
        int dps = state.Effects.DamagePerSecond;
        state.Vitals.Tick(state.Effects.Stats.Get(StatIds.LifeRegen, 0f), suppressNatural: dps > 0);
        if (Session.Clock.Tick % Units.TicksPerSecond == 0)
        {
            if (dps > 0 && p.Alive) state.Vitals.DamageDirect(dps);
            state.Recompute();
        }
    }

    public PlayerEntity AddPlayer(PlayerState state, Vector3 feet)
    {
        var p = new PlayerEntity(state) { Position = feet };
        Players.Add(p);
        return p;
    }

    /// <summary>Respawn position in meters (bed spawn if set and valid, else world spawn).</summary>
    public Vector3 SpawnPositionFor(PlayerState state)
    {
        var cell = Session.SpawnCell;
        if (state.SpawnPoints.TryGetValue(Session.Settings.Name, out var bed) && bed.Length == 3) cell = new Int3(bed[0], bed[1], bed[2]);
        cell = Session.FindStandableNear(cell);
        return new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
    }

    /// <summary>Saves modified chunks, world metadata and every player. Returns chunks written.</summary>
    public int SaveAll()
    {
        int chunks = Session.SaveAll();
        foreach (var p in Players) SavePlayer(p.State);
        return chunks;
    }

    public void SavePlayer(PlayerState state)
    {
        if (PlayerDirectory == null) return;
        Directory.CreateDirectory(PlayerDirectory);
        AtomicFile.WriteAllText(PlayerPath(PlayerDirectory, state.Name), PlayerSave.From(state).Serialize());
    }

    /// <summary>Loads a player file, or creates a new character with the starting kit.</summary>
    public static PlayerState LoadOrCreatePlayer(string? directory, string name, Content.ContentDatabase content)
    {
        if (directory != null)
        {
            var bytes = AtomicFile.ReadWithFallback(PlayerPath(directory, name));
            if (bytes != null)
            {
                var save = PlayerSave.Deserialize(System.Text.Encoding.UTF8.GetString(bytes));
                return save.ToState(content);
            }
        }
        return PlayerState.CreateNew(name, content);
    }

    public static string PlayerPath(string directory, string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return Path.Combine(directory, safe + ".player.json");
    }

    public void Shutdown()
    {
        SaveAll();
        Streamer.UnloadAll();
    }

    public void Dispose() => Jobs.Dispose();
}
