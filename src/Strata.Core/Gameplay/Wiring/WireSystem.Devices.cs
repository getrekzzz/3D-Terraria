using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Wiring;

/// <summary>Wiring devices: timers, teleporters, statues, logic gates and actuators.</summary>
public sealed partial class WireSystem
{
    /// <summary>Most statue creatures alive in the world at once (Terraria caps statue spawns world-wide too).</summary>
    public const int MaxStatueCreatures = 10;

    private readonly Dictionary<TileObjectInstance, long> _timers = new();
    private readonly List<TileObjectInstance> _due = new();
    private readonly Dictionary<long, long> _statueReady = new();
    private readonly List<(PlayerEntity Player, Vector3 Offset)> _playersA = new(), _playersB = new();
    private readonly List<(Entities.Npc Npc, Vector3 Offset)> _npcsA = new(), _npcsB = new();

    private void OnObjectAdded(TileObjectInstance obj)
    {
        if (obj.Def.Timer > 0) _timers[obj] = _world.Session.Clock.Tick + obj.Def.Timer;
    }

    private void OnObjectRemoved(TileObjectInstance obj)
    {
        _timers.Remove(obj);
        _statueReady.Remove(obj.Id);
    }

    /// <summary>
    /// Running timers fire when due. Cost: one dictionary pass over the placed timers. Timers and statue cooldowns whose
    /// chunk unloaded (the store detaches objects without a Removed event) are dropped as they are found.
    /// </summary>
    public void Tick()
    {
        long now = _world.Session.Clock.Tick;
        var objects = _world.Session.World.TileObjects;
        if (now % PruneTicks == 0) PruneStatues(objects);
        if (_timers.Count == 0) return;
        _due.Clear();
        foreach (var (timer, at) in _timers)
            if (objects.Get(timer.Id) != timer || timer.Active && now >= at) _due.Add(timer);
        foreach (var timer in _due)
        {
            if (objects.Get(timer.Id) != timer)
            {
                _timers.Remove(timer); // its chunk unloaded
                continue;
            }
            if (!timer.Active) continue; // switched off by an earlier timer's signal this tick
            _timers[timer] = now + timer.Def.Timer;
            Signal(timer);
        }
    }

    private const int PruneTicks = 600;
    private readonly List<long> _stale = new();

    private void PruneStatues(Voxels.TileObjects.TileObjectStore objects)
    {
        if (_statueReady.Count == 0) return;
        _stale.Clear();
        foreach (var id in _statueReady.Keys)
            if (objects.Get(id) == null) _stale.Add(id);
        foreach (var id in _stale) _statueReady.Remove(id);
    }

    /// <summary>Switches a timer on (it fires one period from now) or off; by hand or by a signal.</summary>
    public void SetTimer(TileObjectInstance timer, bool on)
    {
        timer.Active = on;
        _timers[timer] = _world.Session.Clock.Tick + timer.Def.Timer;
        _world.Interaction.NotifyChanged(timer);
    }

    /// <summary>Swaps whoever stands on pad <paramref name="a"/> with whoever stands on pad <paramref name="b"/>.</summary>
    private void Teleport(TileObjectInstance a, TileObjectInstance b)
    {
        Occupants(a, _playersA, _npcsA);
        Occupants(b, _playersB, _npcsB);
        var topA = PadTop(a);
        var topB = PadTop(b);
        foreach (var (p, off) in _playersA) Move(p, topB + off);
        foreach (var (p, off) in _playersB) Move(p, topA + off);
        foreach (var (n, off) in _npcsA) n.Position = topB + off;
        foreach (var (n, off) in _npcsB) n.Position = topA + off;
    }

    private void Move(PlayerEntity p, Vector3 feet)
    {
        p.Hook.Reset();
        p.Position = feet;
        p.Velocity = Vector3.Zero;
        Teleports++;
        Teleported?.Invoke(p, feet);
    }

    /// <summary>Centre of a pad's top face (metres).</summary>
    private static Vector3 PadTop(TileObjectInstance pad)
    {
        var b = pad.Bounds;
        return new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Max.Y, (b.Min.Z + b.Max.Z) * 0.5f) * Units.CellSize;
    }

    private void Occupants(TileObjectInstance pad, List<(PlayerEntity, Vector3)> players, List<(Entities.Npc, Vector3)> npcs)
    {
        players.Clear();
        npcs.Clear();
        var b = pad.Bounds;
        var top = PadTop(pad);
        const float s = Units.CellSize;
        bool On(Vector3 feet) => feet.X >= b.Min.X * s && feet.X <= b.Max.X * s && feet.Z >= b.Min.Z * s && feet.Z <= b.Max.Z * s
            && feet.Y >= top.Y - 0.1f && feet.Y <= top.Y + s * 2f;
        foreach (var p in _world.Players)
            if (p.Alive && On(p.Position)) players.Add((p, p.Position - top));
        foreach (var n in _world.Npcs.All)
            if (n.Alive && !n.IsBoss && On(n.Position)) npcs.Add((n, n.Position - top));
    }

    /// <summary>A statue releases its creature or item in front of it, within its caps and cooldown.</summary>
    private bool Release(TileObjectInstance statue, StatueDef def)
    {
        long now = _world.Session.Clock.Tick;
        if (_statueReady.TryGetValue(statue.Id, out long ready) && now < ready) return false;
        var center = CenterOf(statue);
        float r2 = def.Radius * Units.CellSize * def.Radius * Units.CellSize;
        var front = new Vector3(center.X, statue.Anchor.Y * Units.CellSize, center.Z) + Facing(statue) * (statue.Footprint.Z * 0.5f + 1f) * Units.CellSize;
        var content = _world.Session.Content;
        if (def.Enemy != null)
        {
            int near = 0, total = 0;
            foreach (var n in _world.Npcs.All)
            {
                if (!n.Alive || !n.FromStatue) continue;
                total++;
                if (n.Def.Id == def.Enemy && Vector3.DistanceSquared(n.Position, center) <= r2) near++;
            }
            if (near + def.Count > def.Cap || total + def.Count > MaxStatueCreatures || !content.Enemies.TryGet(def.Enemy, out var enemy)) return false;
            for (int i = 0; i < def.Count; i++)
                if (_world.Npcs.Spawn(enemy, front) is { } npc) npc.FromStatue = true;
        }
        else if (def.Item != null && content.Items.TryGet(def.Item, out var item))
        {
            int near = 0;
            foreach (var d in _world.Drops.Items)
                if (d.Stack.Item == item && Vector3.DistanceSquared(d.Position, center) <= r2) near += d.Stack.Count;
            if (near + def.Count > def.Cap) return false;
            _world.Drops.Spawn(new ItemStack(item, def.Count), front + new Vector3(0, 0.3f, 0), new Vector3(0, 2f, 0));
        }
        _statueReady[statue.Id] = now + def.Cooldown;
        return true;
    }

    /// <summary>The gate a lamp sits on (through any lamps stacked between), or null.</summary>
    private TileObjectInstance? GateBelow(TileObjectInstance lamp)
    {
        var objects = _world.Session.World.TileObjects;
        var cell = lamp.Anchor - Int3.Up;
        for (int i = 0; i < 64; i++)
        {
            var below = objects.At(cell);
            if (below == null) return null;
            if (below.Def.LogicGate != null) return below;
            if (!below.Def.LogicLamp) return null;
            cell = below.Anchor - Int3.Up;
        }
        return null;
    }

    /// <summary>Gates whose lamps changed recompute their output; a change fires the gate's wires once per chain.</summary>
    private void EvaluateGates()
    {
        if (_gatesToEvaluate.Count == 0) return;
        var objects = _world.Session.World.TileObjects;
        foreach (var gate in _gatesToEvaluate)
        {
            int on = 0, lamps = 0;
            var cell = gate.Anchor + new Int3(0, gate.Footprint.Y, 0);
            for (int i = 0; i < 64 && objects.At(cell) is { Def.LogicLamp: true } lamp; i++)
            {
                lamps++;
                if (lamp.Active) on++;
                cell = lamp.Anchor + new Int3(0, lamp.Footprint.Y, 0);
            }
            bool output = Output(gate.Def.LogicGate!, on, lamps);
            if (output == gate.Active) continue;
            gate.Active = output;
            _world.Interaction.NotifyChanged(gate);
            if (_firedThisChain.Add(gate.Id)) _pending.Enqueue(gate);
        }
        _gatesToEvaluate.Clear();
    }

    /// <summary>A gate's output for <paramref name="on"/> lit lamps of <paramref name="lamps"/>.</summary>
    public static bool Output(string gate, int on, int lamps) => lamps > 0 && gate switch
    {
        "and" => on == lamps,
        "or" => on > 0,
        "nand" => on < lamps,
        "nor" => on == 0,
        "xor" => on == 1,
        "xnor" => on != 1,
        _ => false,
    };

    /// <summary>Applies the actuator toggles gathered by a signal in one edit: blocks switch off (becoming passable air,
    /// remembered with their shape and paint) or back on. A block placed into a switched-off cell meanwhile wins; the
    /// remembered block then drops as an item.</summary>
    private void FlushActuators()
    {
        if (_actuatorToggles.Count == 0) return;
        var world = _world.Session.World;
        var content = _world.Session.Content;
        var batch = world.BeginEdit();
        foreach (var cell in _actuatorToggles)
        {
            if (Grid.Inactive.TryGetValue(cell, out var off))
            {
                ushort now = world.GetBlock(cell);
                if (now == VoxelWorld.Unknown) continue; // not loaded: stays off until it can come back
                Grid.Inactive.Remove(cell);
                if (now == 0 && !world.TileObjects.IsOccupied(cell))
                {
                    batch.SetBlock(cell, off.Block, off.Shape, (CellFlags)(off.Flags & ~CellPaint.Mask));
                    if (CellPaint.Of(off.Flags) != 0) batch.SetPaint(cell, CellPaint.Of(off.Flags));
                    if (content.Blocks[off.Block].IsSolid) batch.SetLiquid(cell, 0);
                }
                else if (content.BlockDrops[off.Block] is { } drop)
                    _world.Drops.Spawn(new ItemStack(drop, 1), (new Vector3(cell.X, cell.Y, cell.Z) + new Vector3(0.5f)) * Units.CellSize, Vector3.Zero);
                continue;
            }
            ushort block = world.GetBlock(cell);
            if (block == 0 || block == VoxelWorld.Unknown) continue;
            Grid.Inactive[cell] = new InactiveBlock(block, world.GetShape(cell.X, cell.Y, cell.Z), (byte)world.GetFlags(cell.X, cell.Y, cell.Z));
            batch.SetBlock(cell, 0);
        }
        _actuatorToggles.Clear();
        _world.Editor.Apply(batch);
        Changed?.Invoke();
    }
}
