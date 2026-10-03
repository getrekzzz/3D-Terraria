using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Conditions;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Simulation;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Visiting merchants (Terraria's Travelling and Skeleton Merchants, P5-19). Town visitors roll their dawn chance once a
/// day (Pcg32 of seed, day and id), walk in from 20–30 m outside a home, stroll around the town centre and walk off at
/// their leave hour. Site visitors roll an hourly chance while the player is where they belong, are placed out of sight
/// like rescues, and vanish once the player has been far away for a while or at the next dawn. Visitors never count as
/// residents, take no home and have no happiness.
/// </summary>
public sealed partial class TownSystem
{
    public const int VisitInterval = 60;

    /// <summary>A site visitor leaves after the player has been this far away (m) for <see cref="SiteAbsentTicks"/>.</summary>
    public const float SiteLeaveDistance = 100f;
    public const int SiteAbsentTicks = 60 * 60;

    /// <summary>A town visitor on the way out is gone once this far from the town centre (m), or after the timeout.</summary>
    public const float LeaveDistance = 40f;
    private const int LeaveTimeoutTicks = 60 * 45;

    private readonly Dictionary<string, (ICondition Requires, ICondition Where, string[] Zones)> _visits = new();
    private long _visitRolledDay = -1;

    /// <summary>A visitor arrived (also raises <see cref="Arrived"/> for the announcement).</summary>
    public event Action<TownNpc>? VisitorArrived;

    /// <summary>A visitor left (also raises <see cref="Departed"/> for the announcement).</summary>
    public event Action<TownNpc>? VisitorLeft;

    /// <summary>Overrides every town visitor's dawn chance (tests); null = data.</summary>
    public float? DawnChanceOverride { get; set; }

    private ICondition VisitCondition(TownNpcDef def, bool where)
    {
        if (!_visits.TryGetValue(def.Id, out var c))
        {
            var v = def.Visit!;
            _visits[def.Id] = c = (ConditionCompiler.CompileAll(v.Requires), ConditionCompiler.CompileAll(v.Where), ZonesOf(v.Where));
        }
        return where ? c.Where : c.Requires;
    }

    /// <summary>Rolls arrivals and sends visitors away (every <see cref="VisitInterval"/> ticks).</summary>
    public void UpdateVisitors()
    {
        var player = _world.LocalPlayer;
        if (player == null) return;
        var clock = _world.Session.Clock;
        for (int i = _residents.Count - 1; i >= 0; i--)
            if (_residents[i].Def.Kind == TownNpcKind.Visitor) TickVisitor(_residents[i], player);
        if (!ArrivalsEnabled) return;
        if (clock.Day != _visitRolledDay && clock.IsDay)
        {
            _visitRolledDay = clock.Day;
            foreach (var def in _world.Session.Content.TownNpcs.All)
                if (def.Visit is { Mode: VisitInfo.TownMode } v && Find(def.Id) == null && clock.Hour < v.LeaveHour
                    && DawnRoll(def, clock.Day) < (DawnChanceOverride ?? v.DawnChance) && VisitCondition(def, false).Evaluate(TownContext(player)))
                    ArriveVisitor(def);
        }
        foreach (var def in _world.Session.Content.TownNpcs.All)
        {
            if (def.Visit is not { Mode: VisitInfo.SiteMode } v || Find(def.Id) != null) continue;
            // Per-check share of the hourly chance: 1 − (1 − p)^(checks per hour)⁻¹.
            float chance = 1f - MathF.Pow(1f - Math.Clamp(v.HourlyChance, 0f, 1f), VisitInterval / (float)GameClock.TicksPerHour);
            if (_rng.NextFloat() >= chance) continue;
            var ctx = TownContext(player);
            if (!VisitCondition(def, false).Evaluate(ctx) || !VisitCondition(def, true).Evaluate(ctx)) continue;
            ArriveVisitor(def, player);
        }
    }

    /// <summary>The visitor's dawn roll for a day, in [0, 1): stable for a world, day and visitor.</summary>
    public float DawnRoll(TownNpcDef def, long day)
    {
        var rng = new Pcg32(Hash.Combine(Hash.Combine((ulong)_world.Session.Settings.Seed, (ulong)day), Hash.String(def.Id)));
        return rng.NextFloat();
    }

    /// <summary>Brings a visitor in now (commands and tests skip the chance and requirements). Null if there is no room.</summary>
    public TownNpc? ArriveVisitor(TownNpcDef def) => ArriveVisitor(def, _world.LocalPlayer);

    private TownNpc? ArriveVisitor(TownNpcDef def, Session.PlayerEntity? player)
    {
        if (def.Visit is not { } v || player == null) return Find(def.Id);
        if (Find(def.Id) is { } here) return here;
        Int3? node;
        Int3? anchor = null;
        if (v.Mode == VisitInfo.SiteMode)
            node = Sites.TryFind(VisitCondition(def, true), _visits[def.Id].Zones, v.MinDistance, v.MaxDistance, player, ref _rng);
        else
        {
            anchor = TownCentre() ?? _world.Navigator.NearestStandable(player.FeetCell, Agent, 6);
            node = anchor is { } a ? Outskirts(a) ?? a : null;
        }
        if (node is not { } at) return null;
        var npc = Create(def, at);
        npc.VisitDay = _world.Session.Clock.Day;
        npc.Anchor = anchor ?? at;
        if (anchor is { } goal) GoTo(npc, goal);
        VisitorArrived?.Invoke(npc);
        Arrived?.Invoke(npc);
        return npc;
    }

    /// <summary>Sends a visitor away (commands): town visitors walk off, site visitors vanish.</summary>
    public bool SendAway(TownNpc npc)
    {
        if (npc.Def.Kind != TownNpcKind.Visitor) return false;
        if (npc.Def.Visit!.Mode == VisitInfo.SiteMode || npc.Anchor == null) RemoveVisitor(npc);
        else StartLeaving(npc);
        return true;
    }

    private void TickVisitor(TownNpc r, Session.PlayerEntity player)
    {
        var v = r.Def.Visit!;
        var clock = _world.Session.Clock;
        if (!r.Alive) { RemoveVisitor(r); return; } // a slain visitor does not come back until their next visit
        if (v.Mode == VisitInfo.SiteMode)
        {
            bool far = Vector3.Distance(player.Position, r.Position) > SiteLeaveDistance;
            r.LeaveTicks = far ? r.LeaveTicks + VisitInterval : 0;
            if (r.LeaveTicks >= SiteAbsentTicks || clock.Day != r.VisitDay && clock.IsDay) RemoveVisitor(r);
            return;
        }
        if (!r.Leaving)
        {
            if (clock.Day != r.VisitDay || clock.Hour >= v.LeaveHour || !clock.IsDay) StartLeaving(r);
            return;
        }
        r.LeaveTicks += VisitInterval;
        var centre = TownCentre() ?? r.FeetCell;
        float gone = Vector3.Distance(r.Position, NodeFeet(centre));
        bool seen = Vector3.Distance(player.Position, r.Position) < 24f;
        if (gone > LeaveDistance || r.LeaveTicks >= LeaveTimeoutTicks && !seen || r.LeaveTicks >= 2 * LeaveTimeoutTicks) RemoveVisitor(r);
    }

    private void StartLeaving(TownNpc r)
    {
        r.Leaving = true;
        r.LeaveTicks = 0;
        if (r.TalkingTo != null) EndTalk(r);
        var exit = Outskirts(TownCentre() ?? r.FeetCell, LeaveDistance + 6f, LeaveDistance + 12f) ?? r.FeetCell;
        r.Anchor = exit;
        GoTo(r, exit);
    }

    private void RemoveVisitor(TownNpc r)
    {
        if (r.TalkingTo != null) EndTalk(r);
        if (!_residents.Remove(r)) return;
        Removed?.Invoke(r);
        VisitorLeft?.Invoke(r);
        if (r.Alive) Departed?.Invoke(r);
    }

    /// <summary>The town centre: the mean of the residents' homes (null without a housed resident).</summary>
    public Int3? TownCentre()
    {
        long x = 0, y = 0, z = 0;
        int n = 0;
        foreach (var r in _residents)
        {
            if (r.Home is not { } h || !r.CountsAsResident) continue;
            x += h.X; y += h.Y; z += h.Z; n++;
        }
        if (n == 0) return null;
        var mean = new Int3((int)(x / n), (int)(y / n), (int)(z / n));
        return _world.Navigator.NearestStandable(mean, Agent, 6) ?? mean;
    }

    /// <summary>A standable, loaded node 20–30 m (or the given shell) from <paramref name="centre"/> in a random direction.</summary>
    private Int3? Outskirts(Int3 centre, float min = 20f, float max = 30f)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = _rng.NextFloat() * MathF.Tau;
            float d = (min + _rng.NextFloat() * (max - min)) / Units.CellSize;
            var target = centre + new Int3((int)(MathF.Cos(a) * d), 0, (int)(MathF.Sin(a) * d));
            if (World.GetBlock(target) == VoxelWorld.Unknown) continue;
            for (int dy = -8; dy <= 8; dy += 4)
                if (_world.Navigator.NearestStandable(target + new Int3(0, dy, 0), Agent, 4) is { } node) return node;
        }
        return null;
    }
}
