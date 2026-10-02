using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Events;

public sealed class ActiveEvent
{
    public ActiveEvent(EventDef def, int ticksLeft)
    {
        Def = def;
        TicksLeft = ticksLeft;
    }

    public EventDef Def { get; }

    /// <summary>Ticks remaining; −1 = until the day/night phase ends.</summary>
    public int TicksLeft { get; set; }

    public int Kills { get; set; }
}

/// <summary>
/// Starts, runs and ends world events: rolls night events at dusk and day events at dawn (chance + data
/// conditions), counts kills towards goals, spawns finales, exposes spawn modifiers and sky tint, and persists
/// state in world.json.
/// </summary>
public sealed class EventDirector
{
    private readonly GameWorld _world;
    private readonly List<ActiveEvent> _active = new();
    private readonly Dictionary<EventDef, ICondition> _conditions = new();
    private bool? _wasDay;
    private bool _placePending;
    private Pcg32 _rng;

    public EventDirector(GameWorld world, NpcSystem npcs)
    {
        _world = world;
        _rng = new Pcg32(Hash.Combine((ulong)world.Session.Settings.Seed, world.Session.Clock.Day + 0x4556L));
        foreach (var e in world.Session.Content.Events.All) _conditions[e] = ConditionCompiler.CompileAll(e.Conditions);
        foreach (var saved in world.Session.Meta.EventStates)
            if (world.Session.Content.Events.TryGet(saved.Id, out var def))
            {
                Add(new ActiveEvent(def, saved.TicksLeft) { Kills = saved.Kills });
                _placePending = true; // on the first tick, once the world's systems are all listening
            }
        npcs.Killed += OnKilled;
        world.Progression.Raised += flag =>
        {
            foreach (var def in world.Session.Content.Events.All)
                if (def.StartsWhen == flag.Id) Start(def);
        };
    }

    public IReadOnlyList<ActiveEvent> Active => _active;

    /// <summary>(message, event) for announcements.</summary>
    public event Action<string, EventDef>? Announced;

    public event Action<EventDef>? Started;
    public event Action<EventDef>? Ended;

    public bool IsActive(string id) => _active.Any(a => a.Def.Id == id);

    public float SpawnRateMultiplier => _active.Aggregate(1f, (m, a) => m * a.Def.SpawnRateMultiplier);

    public int MaxNearbyBonus => _active.Sum(a => a.Def.MaxNearbyBonus);

    /// <summary>Sky tint of the most recently started tinted event, or null.</summary>
    public string? SkyTint => _active.LastOrDefault(a => a.Def.SkyTint != null)?.Def.SkyTint;

    public bool PreventsSleep => _active.Any(a => a.Def.PreventsSleep);

    public void Tick()
    {
        bool day = _world.Session.Clock.IsDay;
        if (_wasDay is { } was && was != day)
        {
            EndPhase(day ? EventPhase.Night : EventPhase.Day);
            Roll(day ? EventPhase.Day : EventPhase.Night);
        }
        _wasDay = day;
        if (_placePending)
        {
            _placePending = false;
            foreach (var a in _active) Place(a.Def);
        }
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var a = _active[i];
            if (a.TicksLeft > 0 && --a.TicksLeft == 0)
            {
                if (a.Def.SpawnBoss != null) CallBoss(a.Def.SpawnBoss);
                End(a, completed: a.Def.KillGoal == 0);
            }
        }
    }

    /// <summary>The kill goal in force for an event (lower once its <see cref="EventDef.KillGoalAfterFlag"/> is set).</summary>
    public int GoalOf(ActiveEvent a) =>
        a.Def.KillGoalAfterFlag is { } after && _world.Progression.IsSet(after.Flag) ? after.Goal : a.Def.KillGoal;

    /// <summary>
    /// An arrival's countdown ran out: the boss comes for the player unless one already lives or it was beaten
    /// meanwhile. Fliers appear 20 m ahead of the player and 12 m up; walkers drop in from 12 m overhead.
    /// </summary>
    private void CallBoss(string id)
    {
        if (!_world.Session.Content.Enemies.TryGet(id, out var boss)) return;
        if (boss.Boss?.Flag is { } flag && _world.Progression.IsSet(flag)) return;
        foreach (var n in _world.Npcs.All)
            if (n.Alive && n.Def == boss) return;
        PlayerEntity? player = null;
        foreach (var p in _world.Players)
            if (p.Alive)
            {
                player = p;
                break;
            }
        if (player == null) return;
        var up = new System.Numerics.Vector3(0, 12f, 0);
        var ahead = AI.BrainFactory.Flat(player.Facing);
        if (ahead == System.Numerics.Vector3.Zero) ahead = System.Numerics.Vector3.UnitX;
        _world.Npcs.Spawn(boss, player.Position + up + (boss.Gravity ? System.Numerics.Vector3.Zero : ahead * 20f));
    }

    /// <summary>Starts an event now (commands, summon items). Returns false if it is already active.</summary>
    public bool Start(EventDef def)
    {
        if (IsActive(def.Id)) return false;
        int ticks = def.Duration > 0 ? def.Duration : -1;
        Add(new ActiveEvent(def, ticks));
        Place(def);
        if (def.StartMessage != null) Announced?.Invoke(def.StartMessage, def);
        Started?.Invoke(def);
        return true;
    }

    public bool Stop(string id)
    {
        var a = _active.FirstOrDefault(x => x.Def.Id == id);
        if (a == null) return false;
        End(a, completed: false);
        return true;
    }

    /// <summary>Writes active event state into the world metadata (called before saving).</summary>
    public void Persist()
    {
        var meta = _world.Session.Meta;
        meta.EventStates = _active.Select(a => new Persistence.WorldMeta.EventStateModel(a.Def.Id, a.TicksLeft, a.Kills)).ToList();
        meta.ActiveEvents = _active.Select(a => a.Def.Id).ToList();
    }

    /// <summary>Rolls the phase's events (dusk or dawn); tests may force every chance with <paramref name="chance"/>.</summary>
    internal void Roll(EventPhase phase, float? chance = null)
    {
        var session = _world.Session;
        var ctx = session.ContextAt(session.SpawnCell);
        _world.Town.FillTownFacts(ctx); // a party needs its host alive and in town
        ctx.PlayerMaxLife = _world.Players.Count == 0 ? 0 : _world.Players.Max(p => p.State.Vitals.MaxLife); // Blood Moon, invasions
        ctx.PlayerDefense = _world.Players.Count == 0 ? 0 : _world.Players.Max(p => p.State.Defense); // the Eye's arrival
        List<(string Group, List<EventDef> Members)>? groups = null;
        foreach (var def in session.Content.Events.All)
        {
            if (def.Phase != phase || def.Chance <= 0 || IsActive(def.Id)) continue;
            ctx.Random = _rng;
            bool ok = _conditions[def].Evaluate(ctx);
            _rng = ctx.Random;
            if (!ok) continue;
            if (def.Group == null)
            {
                if (_rng.Chance(chance ?? def.Chance)) Start(def);
                continue;
            }
            groups ??= new();
            int g = groups.FindIndex(x => x.Group == def.Group);
            if (g < 0) groups.Add((def.Group, new List<EventDef> { def }));
            else groups[g].Members.Add(def);
        }
        if (groups == null) return;
        // A group (the mechanical bosses' warnings) starts at most one member per roll, picked among the eligible.
        foreach (var (group, members) in groups)
        {
            if (_active.Any(a => a.Def.Group == group)) continue;
            var pick = members[_rng.NextInt(members.Count)];
            if (_rng.Chance(chance ?? pick.Chance)) Start(pick);
        }
    }

    private void EndPhase(EventPhase phase)
    {
        foreach (var a in _active.Where(a => a.Def.Phase == phase && a.TicksLeft < 0 && !(a.Def.UntilDefeated && a.Def.KillGoal > 0)).ToList())
            End(a, completed: a.Def.KillGoal == 0);
    }

    private void OnKilled(Npc npc)
    {
        foreach (var a in _active.ToList())
        {
            if (a.Def.KillGoal <= 0 || a.Def.KillTag == null || !npc.Def.HasTag(a.Def.KillTag)) continue;
            _world.Session.EventKills[a.Def.Id] = ++a.Kills;
            if (a.Kills < GoalOf(a)) continue;
            if (a.Def.FinaleEnemy != null && _world.LocalPlayer is { } p && _world.Session.Content.Enemies.TryGet(a.Def.FinaleEnemy, out var boss))
                _world.Npcs.Spawn(boss, p.Position + new System.Numerics.Vector3(0, 12f, 0));
            End(a, completed: true);
        }
    }

    /// <summary>
    /// Stands the event's placed creatures on the surface, evenly around a ring halfway out from the world's centre
    /// (drawn in over the sea); skips any already alive or whose boss flag is set (destroyed before a save).
    /// </summary>
    private void Place(EventDef def)
    {
        var session = _world.Session;
        var plan = session.Generator.Plan;
        int n = def.Places.Length;
        for (int i = 0; i < n; i++)
        {
            if (!session.Content.Enemies.TryGet(def.Places[i], out var enemy)) continue;
            if (enemy.Boss?.Flag is { } flag && _world.Progression.IsSet(flag)) continue;
            if (_world.Npcs.All.Any(x => x.Alive && x.Def == enemy)) continue;
            float angle = MathF.PI * 0.25f + i * MathF.Tau / n, r = MathF.Min(plan.Size.X, plan.Size.Z) * 0.25f;
            float x = 0, z = 0;
            for (int k = 0; k < 12; k++, r *= 0.9f)
            {
                x = plan.Size.X * 0.5f + MathF.Cos(angle) * r;
                z = plan.Size.Z * 0.5f + MathF.Sin(angle) * r;
                if (plan.Landness.Sample(x, z) >= 0.97f) break;
            }
            float y = MathF.Ceiling(session.Generator.Terrain.SurfaceHeight(x, z)) + 2f;
            _world.Npcs.Spawn(enemy, new System.Numerics.Vector3(x, y, z) * Common.Units.CellSize);
        }
    }

    private void Add(ActiveEvent a)
    {
        _active.Add(a);
        _world.Session.Events.Add(a.Def.Id);
        _world.Session.EventKills[a.Def.Id] = a.Kills;
    }

    private void End(ActiveEvent a, bool completed)
    {
        _active.Remove(a);
        _world.Session.Events.Remove(a.Def.Id);
        _world.Session.EventKills.Remove(a.Def.Id);
        if (completed && a.Def.CompletionFlag != null) _world.Progression.Set(a.Def.CompletionFlag);
        if (a.Def.EndMessage != null) Announced?.Invoke(a.Def.EndMessage, a.Def);
        Ended?.Invoke(a.Def);
    }
}
