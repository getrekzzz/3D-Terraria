using System.Numerics;
using Strata.Core.Simulation;

namespace Strata.Core.Gameplay.Town;

/// <summary>Death and return: a fallen (or vanished) resident comes back at the first dawn after they fell (Terraria).</summary>
public sealed partial class TownSystem
{
    private void MarkDead(TownNpc r)
    {
        r.DiedAtTick = _world.Session.Clock.Tick;
        r.RespawnTicks = 0;
        r.Path = null;
        ClearDefense(r); // a fallen resident comes back calm, standing and unwarded
        r.Posture = ResidentPosture.Stand;
        r.Seat = r.SeatTarget = null;
    }

    /// <summary>
    /// With <see cref="TownConfig.RespawnAtDawn"/>: once the first dawn after the death has come and at least
    /// <see cref="TownConfig.MinDeadTicks"/> have passed. Otherwise after <see cref="TownConfig.RespawnTicks"/>.
    /// </summary>
    private bool ShouldRevive(TownNpc r)
    {
        long tick = _world.Session.Clock.Tick;
        if (r.DiedAtTick < 0 || tick < r.DiedAtTick) r.DiedAtTick = tick; // an old save, or the clock was set back
        long dead = tick - r.DiedAtTick;
        if (!Config.RespawnAtDawn) return dead >= Config.RespawnTicks;
        return dead >= Config.MinDeadTicks && tick >= FirstDawnAfter(r.DiedAtTick);
    }

    /// <summary>The tick of the first dawn strictly after <paramref name="tick"/>.</summary>
    public static long FirstDawnAfter(long tick)
    {
        long dayStart = tick / GameClock.TicksPerDay * GameClock.TicksPerDay;
        long dawn = dayStart + (long)(GameClock.DawnHour * GameClock.TicksPerHour);
        return tick >= dawn ? dawn + GameClock.TicksPerDay : dawn;
    }

    private void Revive(TownNpc r)
    {
        var at = r.Home ?? LandmarkNode(r.Def);
        if (at == null && r.Def.Landmark != null)
        {
            r.RespawnTicks = LandmarkInterval; // their place is not loaded: come back once it is
            return;
        }
        at ??= _world.Navigator.NearestStandable(_world.Session.SpawnCell, Agent, 6) ?? _world.Session.SpawnCell;
        r.Life = r.Def.Life;
        r.DiedAtTick = -1;
        r.Position = NodeFeet(at.Value);
        r.Velocity = Vector3.Zero;
        r.Path = null;
        Spawned?.Invoke(r);
    }

    /// <summary>The resident turns into what they summoned: gone (without a death) until the next dawn.</summary>
    public void Vanish(TownNpc r)
    {
        if (r.TalkingTo != null) EndTalk(r);
        r.Life = 0;
        MarkDead(r);
        r.Velocity = default;
    }
}
