using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Session;

/// <summary>
/// Sleeping in a bed (P3-34), as in the reference game: a player lies down in a bed and, while every living player
/// sleeps and nothing threatens them (no boss alive, no invasion with a kill goal, no hostile creature within
/// <see cref="SleepThreatRadius"/>), the clock runs <see cref="SleepTimeScale"/> times as fast. Getting hurt, dying,
/// mounting, grappling or losing the bed wakes the sleeper; the engine also wakes them on movement or use input.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>How many times faster the clock runs while everyone sleeps safely.</summary>
    public const int SleepTimeScale = 5;

    /// <summary>A hostile creature this close (m) to a sleeper keeps time at its normal rate.</summary>
    public const float SleepThreatRadius = 15f;

    /// <summary>Height (m) of a bed's mattress above the floor: where the sleeper's hips rest.</summary>
    public const float MattressHeight = 0.47f;

    /// <summary>The clock's current rate: <see cref="SleepTimeScale"/> while sleep speeds it up, else 1.</summary>
    public int TimeScale { get; private set; } = 1;

    private long _sleepSkipped;

    /// <summary>
    /// The clock's tick less the extra ticks sleep added: it advances exactly once per <see cref="Tick"/>, so cadences
    /// in real time (creature liquid contact, periodic debuff damage, drowning) keep their pace while time runs fast.
    /// Equal to the clock's tick until someone sleeps in this session.
    /// </summary>
    public long RealTick => Session.Clock.Tick - _sleepSkipped;

    /// <summary>Raised when a player lies down (true) or gets up (false).</summary>
    public event Action<PlayerEntity, bool>? SleepChanged;

    /// <summary>Lies a living player down in a bed (from the ground, not riding or hooked); false when they cannot.</summary>
    public bool Sleep(PlayerEntity p, TileObjectInstance bed)
    {
        if (!p.Alive || p.Mount != null || p.Minecart != null || p.Hook.Active) return false;
        p.SleepBed = bed;
        p.Position = BedRest(bed).Feet;
        p.Velocity = Vector3.Zero;
        SleepChanged?.Invoke(p, true);
        return true;
    }

    /// <summary>Gets a sleeper up, standing at the bed's centre; false when they were awake.</summary>
    public bool Wake(PlayerEntity p)
    {
        if (p.SleepBed is not { } bed) return false;
        p.SleepBed = null;
        if (p.Alive)
        {
            var b = bed.Bounds;
            var cell = Session.FindStandableNear(new Int3((b.Min.X + b.Max.X) / 2, b.Min.Y, (b.Min.Z + b.Max.Z) / 2));
            p.Position = new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
        }
        SleepChanged?.Invoke(p, false);
        return true;
    }

    /// <summary>
    /// Where a sleeper lies in a bed: <c>Feet</c> is the body's origin (the hips, on the mattress at the bed's centre)
    /// and <c>Head</c> the horizontal unit direction from the hips to the head (towards the headboard).
    /// </summary>
    public static (Vector3 Feet, Vector3 Head) BedRest(TileObjectInstance bed)
    {
        var b = bed.Bounds;
        var centre = new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Min.Y, (b.Min.Z + b.Max.Z) * 0.5f) * Units.CellSize;
        // The model's headboard is on its local +Z; rotation turns it as ObjectPlacement does (0 +Z, 1 −X, 2 −Z, 3 +X).
        var head = (bed.Rotation & 3) switch { 0 => Vector3.UnitZ, 1 => -Vector3.UnitX, 2 => -Vector3.UnitZ, _ => Vector3.UnitX };
        return (centre + new Vector3(0, MattressHeight, 0) - head * 0.15f, head);
    }

    /// <summary>Per player tick: wakes a sleeper who was hurt, died, mounted, hooked or lost the bed, and pins the body.</summary>
    private void TickSleep(PlayerEntity p)
    {
        if (p.SleepBed is not { } bed) return;
        bool woken = !p.Alive || p.State.Vitals.TicksSinceHurt < 2 || p.Mount != null || p.Minecart != null || p.Hook.Active
            || Session.World.TileObjects.Get(bed.Id) != bed;
        if (woken) { Wake(p); return; }
        if (!p.HasBody) { p.Position = BedRest(bed).Feet; p.Velocity = Vector3.Zero; }
    }

    /// <summary>
    /// After a tick: while sleep speeds time up, the clock gains <see cref="SleepTimeScale"/> − 1 extra ticks. Extra
    /// ticks are only added from a multiple of the scale, so every tick the systems see is one — interval checks
    /// (every 60 or 600 ticks) still come round, just as often in game time.
    /// </summary>
    private void AdvanceSleepClock()
    {
        TimeScale = SleepSpeedsTime() ? SleepTimeScale : 1;
        if (TimeScale <= 1 || Session.Clock.Tick % SleepTimeScale != 0) return;
        long before = Session.Clock.Tick;
        Session.Clock.Advance(SleepTimeScale - 1);
        _sleepSkipped += Session.Clock.Tick - before;
    }

    /// <summary>Every living player sleeps, no boss lives, no kill-goal event runs and no hostile creature is near a sleeper.</summary>
    public bool SleepSpeedsTime()
    {
        bool any = false;
        foreach (var p in Players)
        {
            if (!p.Alive) continue;
            if (!p.Sleeping) return false;
            any = true;
        }
        if (!any) return false;
        foreach (var e in Events.Active)
            if (Events.GoalOf(e) > 0) return false;
        foreach (var npc in Npcs.All)
        {
            if (!npc.Alive || npc.Disguised || npc.Def.HasTag("critter")) continue;
            if (npc.IsBoss) return false;
            foreach (var p in Players)
                if (p.Sleeping && Vector3.Distance(npc.Center, p.Center) < SleepThreatRadius) return false;
        }
        return true;
    }
}
