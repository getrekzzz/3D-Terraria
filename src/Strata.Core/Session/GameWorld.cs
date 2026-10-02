using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Events;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Housing;
using Strata.Core.Gameplay.Liquids;
using Strata.Core.Gameplay.Navigation;
using Strata.Core.Gameplay.Town;
using Strata.Core.Gameplay.Player;
using Strata.Core.Gameplay.Stats;
using Strata.Core.Simulation.Liquids;
using Strata.Core.Streaming;

namespace Strata.Core.Session;

/// <summary>
/// The simulation of one open world, independent of any engine: chunk streaming and jobs, the fixed-tick clock,
/// dropped items, world editing and players. The Godot layer renders it and feeds input; tests and the smoke
/// runner drive it directly.
/// </summary>
public sealed partial class GameWorld : IDisposable
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
        Progression = new Gameplay.Progression.ProgressionSystem(this);
        Mutations = new Gameplay.Progression.MutationDirector(this);
        Spread = new Gameplay.Progression.SpreadSystem(this);
        Maps = new Gameplay.Map.MapSystem(this);
        Npcs = new NpcSystem(this);
        Melee = new MeleeSystem(Npcs, ItemUse);
        Projectiles = new ProjectileSystem(this, Npcs, ItemUse);
        Spawner = new SpawnDirector(this, Npcs);
        Events = new EventDirector(this, Npcs);
        Liquids = new LiquidSimulator(session);
        Housing = new HousingScanner(session.World);
        Navigator = new VoxelNavigator(session.World);
        Town = new TownSystem(this);
        Commerce = new TownCommerce(this, Town);
        Services = new TownServices(this, Commerce);
        LiquidEffects = new LiquidEffects(this);
        Incineration = new Gameplay.Progression.IncinerationSummons(this);
        Objectives = new Gameplay.Progression.WorldObjectives(this);
        Rituals = new Gameplay.Progression.Rituals(this);
        Wires = new Gameplay.Wiring.WireSystem(this);
        PlayerDirectory = playerDirectory;
        Editor.ChunksDirty += dirty => Streamer.MarkDirty(dirty);
        Liquids.ChunksDirty += dirty => Streamer.MarkDirty(dirty, urgent: false);
    }

    public WorldSession Session { get; }
    public JobSystem Jobs { get; }
    public ChunkStreamer Streamer { get; }
    public DroppedItemSystem Drops { get; }
    public WorldEditor Editor { get; }
    public ItemUseTable Uses { get; }
    public ItemUseSystem ItemUse { get; }
    public ObjectInteraction Interaction { get; }
    public Gameplay.Progression.ProgressionSystem Progression { get; }
    public Gameplay.Progression.MutationDirector Mutations { get; }
    public Gameplay.Progression.SpreadSystem Spread { get; }
    /// <summary>Each player's discovered map of this world (revealed as they explore, saved with the world).</summary>
    public Gameplay.Map.MapSystem Maps { get; }
    public NpcSystem Npcs { get; }
    public MeleeSystem Melee { get; }
    public ProjectileSystem Projectiles { get; }
    public SpawnDirector Spawner { get; }
    public EventDirector Events { get; }
    public LiquidSimulator Liquids { get; }
    public HousingScanner Housing { get; }
    public VoxelNavigator Navigator { get; }
    public TownSystem Town { get; }

    /// <summary>Orbs, hearts and altars; meteorites.</summary>
    public Gameplay.Progression.WorldObjectives Objectives { get; }
    public Gameplay.Progression.Rituals Rituals { get; }
    public Gameplay.Wiring.WireSystem Wires { get; }
    public TownCommerce Commerce { get; }
    public TownServices Services { get; }
    public LiquidEffects LiquidEffects { get; }
    public Gameplay.Progression.IncinerationSummons Incineration { get; }
    public string? PlayerDirectory { get; }
    public List<PlayerEntity> Players { get; } = new();

    public PlayerEntity? LocalPlayer => Players.Count > 0 ? Players[0] : null;

    /// <summary>Raised after an autosave with the number of chunks written.</summary>
    public event Action<int>? Autosaved;

    public event Action<PlayerEntity>? PlayerDied;

    /// <summary>A player collected a dropped stack (count actually stored).</summary>
    public event Action<PlayerEntity, ItemStack>? ItemPickedUp;

    /// <summary>Raised when a player respawns (the engine moves the body to <see cref="PlayerEntity.Position"/>).</summary>
    public event Action<PlayerEntity>? PlayerRespawned;

    public const int RespawnDelayTicks = Units.TicksPerSecond * 5;

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
        using (Diagnostics.Metrics.Frame.Time("tick.drops"))
        {
            if (player is { Alive: true })
                Drops.Tick(Session.World, dt, player.Center, stack =>
                {
                    var rest = player.State.Inventory.Pickup(stack);
                    if (rest.Count < stack.Count) ItemPickedUp?.Invoke(player, stack.WithCount(stack.Count - rest.Count));
                    return rest;
                });
            else
                Drops.Tick(Session.World, dt, new Vector3(float.MaxValue), s => s);
        }
        var prof = Diagnostics.Metrics.Frame;
        using (prof.Time("tick.players")) foreach (var p in Players) TickPlayer(p);
        using (prof.Time("tick.map")) Maps.Tick();
        using (prof.Time("tick.liquids")) Liquids.Tick(Session.Clock.Tick);
        using (prof.Time("tick.liquid_effects")) LiquidEffects.Tick();
        using (prof.Time("tick.events")) Events.Tick();
        using (prof.Time("tick.spawner")) Spawner.Tick();
        using (prof.Time("tick.npcs")) Npcs.Tick();
        using (prof.Time("tick.town")) Town.Tick();
        using (prof.Time("tick.melee")) Melee.Tick();
        using (prof.Time("tick.projectiles")) Projectiles.Tick();
        using (prof.Time("tick.world"))
        {
            Mutations.Tick();
            Objectives.Tick();
            if (Session.Clock.Tick % 60 == 0) ApplyAuras();
            Rituals.Tick();
            Spread.Tick();
        }
        if (Session.Clock.Tick % 600 == 0) Editor.Damage.Prune(Session.Clock.Tick);
        if (++_autosaveCounter >= AutosaveTicks)
        {
            _autosaveCounter = 0;
            using (prof.Time("tick.autosave")) Autosaved?.Invoke(SaveAll());
        }
    }

    private void TickPlayer(PlayerEntity p)
    {
        var state = p.State;
        if (!p.Alive)
        {
            p.Hook.Reset(); // the chain does not hang from a corpse or follow it to the respawn point
            if (p.RespawnTicks == 0)
            {
                p.RespawnTicks = RespawnDelayTicks;
                state.Deaths++;
                state.Buffs.ClearNonPersistent();
                state.Recompute(); // no debuff effect (confusion) lingers on the corpse
                ApplyDeathPenalty(p);
                DropTombstone(p);
                PlayerDied?.Invoke(p);
            }
            else if (--p.RespawnTicks == 0 && !p.Lost)
            {
                Respawn(p);
            }
            return;
        }
        state.PlayTicks++;
        StepOnPlates(p);
        TouchHazards(p);
        if (p.UseCooldown > 0) p.UseCooldown--;
        if (p.GloveCooldown > 0) p.GloveCooldown--;
        if (p.UseAnimation > 0) p.UseAnimation--;
        // An engine body advances its own hook in its physics step (it applies the pull); headless players tick here.
        if (!p.HasBody && p.Hook.Active) p.Hook.Tick(Session.World, Units.SecondsPerTick, p.Hand);
        if (!p.HasBody && p.ExternalVelocity != Vector3.Zero) p.Position += p.ExternalVelocity * Units.SecondsPerTick; // a tongue's drag
        if (state.Buffs.Tick()) state.Recompute();
        int dps = state.Effects.DamagePerSecond;
        state.Vitals.Tick(state.Effects.Stats.Get(StatIds.LifeRegen, 0f), suppressNatural: dps > 0);
        if (Session.Clock.Tick % Units.TicksPerSecond == 0)
        {
            if (dps > 0 && p.Alive) state.Vitals.DamageDirect(dps);
            state.Recompute();
            CheckMilestones(p);
        }
    }

    public void Respawn(PlayerEntity p)
    {
        p.RespawnTicks = 0;
        p.Breath = PlayerEntity.MaxBreath;
        p.State.Recompute();
        p.State.Vitals.Life = p.State.Vitals.MaxLife;
        p.State.Vitals.Mana = p.State.Vitals.MaxMana;
        p.Position = SpawnPositionFor(p.State);
        p.Velocity = Vector3.Zero;
        p.State.Vitals.ImmunityTicks = RespawnImmunityTicks;
        // Whatever was waiting at the spawn point (usually what killed the player) wanders off.
        foreach (var npc in Npcs.All.Where(n => !n.IsBoss && !n.IsPart && !n.Def.HasTag("critter") && Vector3.Distance(n.Center, p.Center) < RespawnClearRadius).ToList())
            Npcs.Despawn(npc);
        PlayerRespawned?.Invoke(p);
    }

    /// <summary>Invulnerability after respawning, as in the reference game.</summary>
    public const int RespawnImmunityTicks = Units.TicksPerSecond * 3;

    /// <summary>Ordinary creatures this close to the respawn point leave when a player respawns.</summary>
    public const float RespawnClearRadius = 20f;

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

    public void Shutdown()
    {
        SaveAll();
        Streamer.UnloadAll();
    }

    public void Dispose() => Jobs.Dispose();
}
