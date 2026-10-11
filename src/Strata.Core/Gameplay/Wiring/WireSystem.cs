using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Wiring;

/// <summary>
/// Wire in four colours (Red, Blue, Green, Yellow Wrench), each its own network. Flipping a lever or switch, stepping
/// on a pressure plate, a running timer or a logic gate whose output changes sends a signal along every wire colour
/// touching the trigger (6-connected, per colour): doors swing, lights toggle, dart traps fire, actuators switch their
/// blocks off and on, timers start and stop, teleporters swap whoever stands on them, statues release creatures and
/// logic lamps toggle (the gate beneath re-evaluates). Device behaviour is in <c>WireSystem.Devices.cs</c>.
/// </summary>
public sealed partial class WireSystem
{
    /// <summary>Most cells one colour's signal walks (keeps a runaway grid from stalling a tick).</summary>
    public const int MaxSignalCells = 4096;

    /// <summary>Most chained signals (gates feeding gates) one trigger may cause; each gate fires once per chain.</summary>
    public const int MaxChainSignals = 256;

    private readonly GameWorld _world;
    private readonly Queue<Int3> _open = new();
    private readonly HashSet<Int3> _seen = new();
    private readonly List<Int3> _reached = new();
    private readonly HashSet<long> _hitIds = new();
    private readonly List<TileObjectInstance> _teleporters = new();
    private readonly Queue<TileObjectInstance> _pending = new();
    private readonly HashSet<long> _firedThisChain = new();
    private readonly HashSet<TileObjectInstance> _gatesToEvaluate = new();
    private readonly HashSet<Int3> _actuatorToggles = new();
    private bool _busy;

    public WireSystem(GameWorld world)
    {
        _world = world;
        var objects = world.Session.World.TileObjects;
        objects.Added += OnObjectAdded;
        objects.Removed += OnObjectRemoved;
        foreach (var obj in objects.All) OnObjectAdded(obj);
    }

    public WireGrid Grid => _world.Session.Wires;

    /// <summary>Wiring changed (wire, actuators or switched-off blocks): renderers rebuild.</summary>
    public event Action? Changed;

    /// <summary>A signal reached these objects (sounds, sparks).</summary>
    public event Action<IReadOnlyList<TileObjectInstance>>? Signaled;

    /// <summary>A teleporter moved a player to these feet (the engine moves the body).</summary>
    public event Action<PlayerEntity, Vector3>? Teleported;

    /// <summary>Cells carrying any wire.</summary>
    public bool Has(Int3 cell) => Grid.HasAnyWire(cell);

    public bool Has(Int3 cell, WireColor color) => Grid.Has(cell, color);

    /// <summary>Wired or actuated cells.</summary>
    public IReadOnlyCollection<Int3> All => Grid.Keys;

    /// <summary>Teleports so far (probes).</summary>
    public int Teleports { get; private set; }

    public bool Place(Int3 cell, WireColor color = WireColor.Red)
    {
        if (!Grid.Add(cell, color)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes one colour from a cell.</summary>
    public bool Cut(Int3 cell, WireColor color)
    {
        if (!Grid.Remove(cell, color)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes every wire colour and the actuator from a cell (a switched-off block comes back). Returns the
    /// number of wires removed plus one for an actuator.</summary>
    public int Cut(Int3 cell)
    {
        int n = 0;
        foreach (var c in WireColors.All)
            if (Grid.Remove(cell, c)) n++;
        if (RemoveActuator(cell)) n++;
        if (n > 0) Changed?.Invoke();
        return n;
    }

    /// <summary>Puts an actuator on the block in a cell (false when there is no block or one is there already).</summary>
    public bool PlaceActuator(Int3 cell)
    {
        if (Grid.HasActuator(cell)) return false;
        ushort block = _world.Session.World.GetBlock(cell);
        if ((block == 0 || block == Voxels.VoxelWorld.Unknown) && !Grid.Inactive.ContainsKey(cell)) return false;
        Grid.SetActuator(cell, true);
        Changed?.Invoke();
        return true;
    }

    private bool RemoveActuator(Int3 cell)
    {
        // A switched-off block in an unloaded chunk could not be restored: keep its actuator until the chunk is back.
        if (Grid.Inactive.ContainsKey(cell) && _world.Session.World.GetBlock(cell) == Voxels.VoxelWorld.Unknown) return false;
        if (!Grid.SetActuator(cell, false)) return false;
        if (Grid.Inactive.ContainsKey(cell))
        {
            _actuatorToggles.Add(cell);
            FlushActuators();
        }
        return true;
    }

    /// <summary>
    /// Sends a signal from a trigger object through the wires of every colour touching it. Returns the objects it
    /// activated directly; signals it sets off (logic gates) run afterwards in the same call.
    /// </summary>
    public IReadOnlyList<TileObjectInstance> Signal(TileObjectInstance trigger)
    {
        var hits = new List<TileObjectInstance>();
        if (_busy)
        {
            // A signal raised while one is being processed (a gate's output) joins the chain.
            _pending.Enqueue(trigger);
            return hits;
        }
        _busy = true;
        _firedThisChain.Clear();
        try
        {
            SignalOnce(trigger, hits);
            for (int n = 0; _pending.Count > 0 && n < MaxChainSignals; n++)
                SignalOnce(_pending.Dequeue(), null);
            FlushActuators();
        }
        finally
        {
            // Nothing from an interrupted chain may leak into the next one.
            _pending.Clear();
            _gatesToEvaluate.Clear();
            _actuatorToggles.Clear();
            _busy = false;
        }
        return hits;
    }

    private void SignalOnce(TileObjectInstance trigger, List<TileObjectInstance>? hits)
    {
        var objects = _world.Session.World.TileObjects;
        List<TileObjectInstance>? reachedObjects = null;
        foreach (var color in WireColors.All)
        {
            Walk(trigger.Bounds, color, trigger.Def.LogicGate != null);
            if (_reached.Count == 0) continue;
            _hitIds.Clear();
            _hitIds.Add(trigger.Id);
            _teleporters.Clear();
            foreach (var cell in _reached)
            {
                if (Grid.HasActuator(cell) && !_actuatorToggles.Remove(cell)) _actuatorToggles.Add(cell);
                if (objects.At(cell) is not { } obj || !_hitIds.Add(obj.Id) || !Activate(obj)) continue;
                (reachedObjects ??= new List<TileObjectInstance>()).Add(obj);
                hits?.Add(obj);
            }
            if (_teleporters.Count >= 2) Teleport(_teleporters[0], _teleporters[1]);
        }
        EvaluateGates();
        if (reachedObjects != null) Signaled?.Invoke(reachedObjects);
    }

    /// <summary>
    /// Fills <see cref="_reached"/> with the cells of <paramref name="color"/> wire connected to any wire of that colour
    /// inside <paramref name="box"/> or sharing a face with it. A logic gate does not pick up wire running through the
    /// lamps stacked on it (that wire feeds the lamps).
    /// </summary>
    private void Walk(Aabb3i box, WireColor color, bool skipLamps)
    {
        _open.Clear();
        _seen.Clear();
        _reached.Clear();
        var outer = box.Expand(1);
        var objects = _world.Session.World.TileObjects;
        for (int y = outer.Min.Y; y < outer.Max.Y; y++)
            for (int z = outer.Min.Z; z < outer.Max.Z; z++)
                for (int x = outer.Min.X; x < outer.Max.X; x++)
                {
                    int outside = (x < box.Min.X || x >= box.Max.X ? 1 : 0) + (y < box.Min.Y || y >= box.Max.Y ? 1 : 0) + (z < box.Min.Z || z >= box.Max.Z ? 1 : 0);
                    if (outside > 1) continue; // edges and corners only touch diagonally
                    var cell = new Int3(x, y, z);
                    if (!Grid.Has(cell, color) || skipLamps && objects.At(cell) is { Def.LogicLamp: true }) continue;
                    if (_seen.Add(cell)) _open.Enqueue(cell);
                }
        while (_open.Count > 0 && _reached.Count < MaxSignalCells)
        {
            var c = _open.Dequeue();
            _reached.Add(c);
            foreach (var o in Faces.Offsets)
            {
                var n = c + o;
                if (Grid.Has(n, color) && _seen.Add(n)) _open.Enqueue(n);
            }
        }
    }

    private bool Activate(TileObjectInstance obj)
    {
        var def = obj.Def;
        if (def.Timer > 0)
        {
            SetTimer(obj, !obj.Active);
            return true;
        }
        if (def.Teleporter)
        {
            _teleporters.Add(obj);
            return true;
        }
        if (def.Statue is { } statue) return Release(obj, statue);
        if (def.LogicLamp)
        {
            obj.Active = !obj.Active;
            _world.Interaction.NotifyChanged(obj);
            if (GateBelow(obj) is { } gate) _gatesToEvaluate.Add(gate);
            return true;
        }
        if (def.LogicGate != null) return false; // gates read their lamps, not the wire
        if (def.Interaction == TileInteraction.Door)
            return _world.Interaction.SetDoor(obj, !obj.Active, Array.Empty<(Vector3, Vector3)>());
        if (def.Trap is { } trap) return FireTrap(obj, trap);
        if (def.Interaction == TileInteraction.Switch) return false; // switches only send, never receive
        if (def.Light is { Length: 3 } l && (l[0] | l[1] | l[2]) != 0)
        {
            obj.Active = !obj.Active; // a lit light is "inactive"; wiring turns it off and on
            _world.Editor.Relight(obj);
            _world.Interaction.NotifyChanged(obj);
            return true;
        }
        return false;
    }

    private bool FireTrap(TileObjectInstance obj, TrapDef trap)
    {
        if (!_world.Session.Content.Projectiles.TryGet(trap.Projectile, out var projectile)) return false;
        var facing = Facing(obj);
        var from = CenterOf(obj) + facing * (Units.CellSize * 0.6f);
        _world.Projectiles.Spawn(projectile, from, facing * trap.Speed, null, trap.Damage, 2f, DamageClass.None, 0);
        return true;
    }

    /// <summary>An object's front faces whoever placed it: rotation 0 faces −Z, 1 +X, 2 +Z, 3 −X.</summary>
    private static Vector3 Facing(TileObjectInstance obj) =>
        (obj.Rotation & 3) switch { 0 => -Vector3.UnitZ, 1 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitX };

    private static Vector3 CenterOf(TileObjectInstance obj)
    {
        var b = obj.Bounds;
        return new Vector3(b.Min.X + b.Max.X, b.Min.Y + b.Max.Y, b.Min.Z + b.Max.Z) * 0.5f * Units.CellSize;
    }
}
