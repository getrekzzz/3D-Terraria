using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Simulation;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Seating and the night routine (Terraria): at night a housed resident who has reached home sits in a free chair of
/// their room and, from <see cref="BedHour"/>, lies down in a free bed instead. By day an idle resident at home sits a
/// while with <see cref="DaySitChance"/>. They get up at dawn (plus a personal delay of up to an hour), when talked to,
/// when a threat comes into sight, or when the seat is removed. Two residents never share a seat. While seated the
/// resident's feet are snapped onto the seat (<see cref="SeatPoint"/>) facing out of it.
/// </summary>
public sealed partial class TownSystem
{
    public const float BedHour = 22f;
    public const float DaySitChance = 0.3f;
    private const int SeatWalkTimeout = 900;
    private const float SeatReach = 0.6f;

    /// <summary>Keeps a seated resident on their seat; returns true while they stay seated (no errands or steering).</summary>
    private bool UpdateSeat(TownNpc r)
    {
        if (r.Seat is { } id)
        {
            var seat = World.TileObjects.Get(id);
            if (seat == null || ShouldStand(r, seat))
            {
                StandUp(r);
                return false;
            }
            r.Position = SeatPoint(seat);
            r.Velocity = Vector3.Zero;
            r.Yaw = SeatYaw(seat);
            if (r.Posture == ResidentPosture.Sit && !seat.Def.HasHousing("bed") && WantsBed(r) && FreeSeat(r, "bed") != null)
                StandUp(r); // bedtime: leave the chair for a free bed
            return r.Seat != null;
        }
        if (r.Posture != ResidentPosture.Stand) return true; // posed by a command: hold still
        if (r.SeatTarget is { } target) return WalkToSeat(r, target);
        return false;
    }

    /// <summary>Called when a resident idles at home: maybe take a seat (at night always, by day sometimes).</summary>
    private bool TrySit(TownNpc r)
    {
        if (r.Home is not { } home || r.TalkingTo != null || r.Threat != null || r.FleeTicks > 0) return false;
        if (Vector3.Distance(r.Position, NodeFeet(home)) > 3f) return false;
        bool night = !_world.Session.Clock.IsDay;
        if (!night && _rng.NextFloat() >= DaySitChance) return false;
        var seat = (WantsBed(r) ? FreeSeat(r, "bed") : null) ?? FreeSeat(r, "chair");
        if (seat == null) return false;
        r.SeatTarget = seat.Id;
        r.SeatTimer = SeatWalkTimeout;
        r.SeatApproach = _world.Navigator.NearestStandable(seat.Anchor, Agent, 3);
        if (r.SeatApproach is { } node) GoTo(r, node);
        return true;
    }

    private bool WalkToSeat(TownNpc r, long id)
    {
        var seat = World.TileObjects.Get(id);
        if (seat == null || --r.SeatTimer <= 0 || r.TalkingTo != null || r.Threat != null || Taken(r, id))
        {
            r.SeatTarget = null;
            return false;
        }
        var approach = r.SeatApproach is { } n ? NodeFeet(n) : SeatPoint(seat);
        var flat = new Vector2(approach.X - r.Position.X, approach.Z - r.Position.Z);
        if (r.Path != null && flat.Length() > SeatReach) return false; // still walking there
        if (flat.Length() > SeatReach * 2f) return false; // stuck short of it: the timeout gives up
        r.SeatTarget = null;
        r.Seat = id;
        r.Path = null;
        r.Posture = seat.Def.HasHousing("bed") ? ResidentPosture.Sleep : ResidentPosture.Sit;
        r.IdleTicks = _world.Session.Clock.IsDay ? _rng.Range(Config.IdleTicksMin, Config.IdleTicksMax) * 2 : int.MaxValue / 2;
        r.Position = SeatPoint(seat);
        r.Velocity = Vector3.Zero;
        r.Yaw = SeatYaw(seat);
        return true;
    }

    private bool ShouldStand(TownNpc r, TileObjectInstance seat)
    {
        if (r.TalkingTo != null || r.Threat != null || r.FleeTicks > 0 || r.Home == null) return true;
        var clock = _world.Session.Clock;
        if (!clock.IsDay) return false;
        if (r.IdleTicks != int.MaxValue / 2) return --r.IdleTicks <= 0; // a daytime rest
        return clock.Hour >= GameClock.DawnHour + WakeDelay(r) || clock.Hour >= GameClock.DuskHour;
    }

    /// <summary>Gets up: posture back to standing, onto a free node beside the seat.</summary>
    internal void StandUp(TownNpc r)
    {
        bool resting = r.Posture is ResidentPosture.Sit or ResidentPosture.Sleep;
        if (r.Seat == null && r.SeatTarget == null && !resting) return;
        r.Seat = null;
        r.SeatTarget = null;
        if (resting)
        {
            r.Posture = ResidentPosture.Stand;
            var node = r.SeatApproach ?? _world.Navigator.NearestStandable(Physics.VoxelMotion.CellOf(r.Position), Agent, 3);
            if (node is { } n) r.Position = NodeFeet(n);
        }
        r.SeatApproach = null;
        r.IdleTicks = Config.IdleTicksMin;
    }

    private bool WantsBed(TownNpc r)
    {
        var clock = _world.Session.Clock;
        return !clock.IsDay && (clock.Hour >= BedHour || clock.Hour < GameClock.DawnHour);
    }

    /// <summary>A personal wake-up delay (hours, 0..1) so the town does not rise as one.</summary>
    private static float WakeDelay(TownNpc r) => (int)((ulong)(r.Id * 2654435761L) % 60UL) / 60f;

    /// <summary>A free seat with a housing role in the resident's home room, nearest first.</summary>
    private TileObjectInstance? FreeSeat(TownNpc r, string role)
    {
        if (r.Home is not { } home || _world.Rooms.Get(HomeCell(home)) is not { Valid: true } room) return null;
        TileObjectInstance? best = null;
        float bestSq = float.MaxValue;
        foreach (var o in room.Furniture)
        {
            if (!o.Def.HasHousing(role) || World.TileObjects.Get(o.Id) != o || Taken(r, o.Id)) continue;
            float d = Vector3.DistanceSquared(SeatPoint(o), r.Position);
            if (d < bestSq) (bestSq, best) = (d, o);
        }
        return best;
    }

    private bool Taken(TownNpc self, long id)
    {
        foreach (var o in _residents)
            if (o != self && (o.Seat == id || o.SeatTarget == id)) return true;
        return false;
    }

    /// <summary>Where a resident's feet go on a seat: the footprint's centre, raised to the seat (or mattress) height.</summary>
    public static Vector3 SeatPoint(TileObjectInstance seat)
    {
        var f = seat.Footprint;
        float h = seat.Def.HasHousing("bed") ? seat.Def.BedHeight : seat.Def.SeatHeight;
        return new Vector3(seat.Anchor.X + f.X * 0.5f, seat.Anchor.Y + h, seat.Anchor.Z + f.Z * 0.5f) * Units.CellSize;
    }

    /// <summary>
    /// The way a seat faces: objects are placed facing the player who placed them (rotation 0 = placed looking +Z), so
    /// a sitter looks back along the placer's view.
    /// </summary>
    public static float SeatYaw(TileObjectInstance seat) => BrainFactory.YawOf((seat.Rotation & 3) switch
    {
        0 => new Vector3(0, 0, -1),
        1 => new Vector3(1, 0, 0),
        2 => new Vector3(0, 0, 1),
        _ => new Vector3(-1, 0, 0),
    });
}
