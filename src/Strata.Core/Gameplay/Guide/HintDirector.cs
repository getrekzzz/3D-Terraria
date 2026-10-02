using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Simulation;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Guide;

/// <summary>
/// Onboarding hints (data/hints.json), checked once a second per player: the highest-priority hint whose conditions
/// hold — achievement triggers on the journal and world, time of day, a carried item, something nearby, the Guide's
/// next step — is shown once per character (remembered in <see cref="PlayerJournal.HintsShown"/>, saved with the
/// player). At most one hint per <see cref="CooldownSeconds"/>, none before a hint's <see cref="HintDef.Delay"/>.
/// Deterministic: hints are tried in (priority descending, id) order and timing counts checks, not the world clock.
/// </summary>
public sealed class HintDirector
{
    public const int DefaultCooldownSeconds = 25;

    /// <summary>No hint in a player's first seconds in a world (the loading screen is still fading).</summary>
    public const int StartSeconds = 3;

    private readonly GameWorld _world;
    private readonly List<Compiled> _hints = new();
    private readonly Dictionary<PlayerEntity, Timing> _timing = new();
    private bool _enabled = true;

    private sealed class Compiled
    {
        public required HintDef Def;
        public required GuideTrigger[] When;
        public required GuideTrigger[] Unless;
        public ItemDef? Carrying;
    }

    private sealed class Timing
    {
        public long Seconds;            // checks since this player joined (one a second)
        public long LastShown = long.MinValue / 2;
    }

    public HintDirector(GameWorld world)
    {
        _world = world;
        var content = world.Session.Content;
        foreach (var def in content.Hints.All)
        {
            try
            {
                _hints.Add(new Compiled
                {
                    Def = def,
                    When = Array.ConvertAll(def.When, GuideTrigger.Compile),
                    Unless = Array.ConvertAll(def.Unless, GuideTrigger.Compile),
                    Carrying = def.Carrying != null && content.Items.TryGet(def.Carrying, out var item) ? item : null,
                });
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
            {
                Diagnostics.LogExtensions.Warn(world.Session.Log, "guide", $"hint {def.Id}: {e.Message}"); // the validator reports it
            }
        }
        _hints.Sort((a, b) => a.Def.Priority != b.Def.Priority
            ? b.Def.Priority.CompareTo(a.Def.Priority)
            : string.CompareOrdinal(a.Def.Id, b.Def.Id));
    }

    /// <summary>False turns every hint off (the interface setting); nothing is marked shown while off.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            EnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Hints were turned on or off (the game saves it as the interface setting).</summary>
    public event Action<bool>? EnabledChanged;

    public int CooldownSeconds { get; set; } = DefaultCooldownSeconds;

    /// <summary>A hint was shown to a player (the hint toast listens).</summary>
    public event Action<PlayerEntity, HintDef>? Shown;

    /// <summary>The last hint shown to any player this session (for probes and the console).</summary>
    public HintDef? LastShown { get; private set; }

    /// <summary>Called once a second per player.</summary>
    public void Check(PlayerEntity p)
    {
        var t = TimingOf(p);
        t.Seconds++;
        if (!Enabled || !p.Alive || t.Seconds - t.LastShown < CooldownSeconds) return;
        var shown = p.State.Journal.HintsShown;
        for (int i = 0; i < _hints.Count; i++)
        {
            var h = _hints[i];
            if (shown.Contains(h.Def.Id) || t.Seconds < MathF.Max(h.Def.Delay, StartSeconds) || !Due(h, p)) continue;
            Show(p, h.Def, t);
            return;
        }
    }

    /// <summary>Shows a hint now regardless of its conditions and the cooldown (the <c>hint show</c> command).</summary>
    public void Force(PlayerEntity p, HintDef def) => Show(p, def, TimingOf(p));

    /// <summary>Whether a hint's conditions hold for the player right now (ignores shown-once, delay and cooldown).</summary>
    public bool IsDue(PlayerEntity p, HintDef def)
    {
        for (int i = 0; i < _hints.Count; i++)
            if (_hints[i].Def == def) return Due(_hints[i], p);
        return false;
    }

    /// <summary>Forgets a player's shown hints and timing (the <c>hint reset</c> command).</summary>
    public void Reset(PlayerEntity p)
    {
        p.State.Journal.HintsShown.Clear();
        _timing.Remove(p);
    }

    private void Show(PlayerEntity p, HintDef def, Timing t)
    {
        p.State.Journal.HintsShown.Add(def.Id);
        t.LastShown = t.Seconds;
        LastShown = def;
        Shown?.Invoke(p, def);
    }

    private Timing TimingOf(PlayerEntity p)
    {
        if (!_timing.TryGetValue(p, out var t)) _timing[p] = t = new Timing();
        return t;
    }

    // Cheapest conditions first; the nearby scan last.
    private bool Due(Compiled h, PlayerEntity p)
    {
        var def = h.Def;
        if (!TimeHolds(def.Time, _world.Session.Clock)) return false;
        if (h.Carrying != null && p.State.Inventory.Backpack.Count(h.Carrying) == 0) return false;
        for (int i = 0; i < h.When.Length; i++) if (!h.When[i].Met(_world, p)) return false;
        for (int i = 0; i < h.Unless.Length; i++) if (h.Unless[i].Met(_world, p)) return false;
        if (def.Step != null && !IsNextStep(def.Step, p)) return false;
        return def.Near == null || IsNear(def.Near, p);
    }

    private static bool TimeHolds(HintTime time, GameClock clock) => time switch
    {
        HintTime.Day => clock.IsDay,
        HintTime.Night => !clock.IsDay,
        HintTime.Dusk => clock.Hour >= GameClock.DuskHour - HintDef.DuskHours && clock.Hour < GameClock.DuskHour,
        _ => true,
    };

    private bool IsNextStep(string step, PlayerEntity p)
    {
        var next = _world.Advisor.Next(p);
        return next.Count > 0 && next[0].Id == step;
    }

    private bool IsNear(HintNear near, PlayerEntity p)
    {
        if (near.Resident != null)
        {
            float r2 = near.Radius * near.Radius;
            var residents = _world.Town.Residents;
            for (int i = 0; i < residents.Count; i++)
            {
                var r = residents[i];
                if (r.Alive && r.Def.Id == near.Resident && Vector3.DistanceSquared(r.Center, p.Center) <= r2) return true;
            }
        }
        if (near.Object == null && near.Tag == null && !near.Tree) return false;
        int cells = (int)MathF.Ceiling(near.Radius * Units.CellsPerMeter);
        var f = p.FeetCell;
        foreach (var o in _world.Session.World.TileObjects.InBox(new Aabb3i(f - Int3.One * cells, f + Int3.One * (cells + 1))))
        {
            var d = o.Def;
            if (d.Id == near.Object || (near.Tag != null && d.HasTag(near.Tag))
                || (near.Tree && d.Collision == TileCollision.Trunk && d.BreakTool == BreakTool.Axe)) return true;
        }
        return false;
    }
}
