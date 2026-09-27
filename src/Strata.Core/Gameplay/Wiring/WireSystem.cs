using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Wiring;

/// <summary>
/// Red wire laid through cells with the Red Wrench. Flipping a lever or switch, or stepping on a pressure plate,
/// sends a signal along every wire touching the trigger (6-connected): doors swing, lights toggle and dart traps
/// fire wherever the wire runs through them.
/// </summary>
public sealed class WireSystem
{
    /// <summary>Most cells one signal walks (keeps a runaway grid from stalling a tick).</summary>
    public const int MaxSignalCells = 4096;

    private readonly GameWorld _world;

    public WireSystem(GameWorld world) => _world = world;

    private HashSet<Int3> Wires => _world.Session.Wires;

    /// <summary>The set of wired cells changed (renderers rebuild).</summary>
    public event Action? Changed;

    /// <summary>A signal reached these objects (sounds, sparks).</summary>
    public event Action<IReadOnlyList<TileObjectInstance>>? Signaled;

    public bool Has(Int3 cell) => Wires.Contains(cell);

    public IReadOnlyCollection<Int3> All => Wires;

    public bool Place(Int3 cell)
    {
        if (!Wires.Add(cell)) return false;
        Changed?.Invoke();
        return true;
    }

    public bool Cut(Int3 cell)
    {
        if (!Wires.Remove(cell)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Sends a signal from a trigger object through the wires touching it. Returns the objects activated.</summary>
    public IReadOnlyList<TileObjectInstance> Signal(TileObjectInstance trigger)
    {
        var reached = Walk(trigger.Bounds);
        var hit = new List<TileObjectInstance>();
        var seen = new HashSet<long> { trigger.Id };
        var objects = _world.Session.World.TileObjects;
        foreach (var cell in reached)
            if (objects.At(cell) is { } obj && seen.Add(obj.Id) && Activate(obj)) hit.Add(obj);
        if (hit.Count > 0) Signaled?.Invoke(hit);
        return hit;
    }

    /// <summary>Wired cells connected to any wire inside or face-adjacent to <paramref name="box"/>.</summary>
    private List<Int3> Walk(Aabb3i box)
    {
        var queue = new Queue<Int3>();
        var seen = new HashSet<Int3>();
        var outer = box.Expand(1);
        for (int y = outer.Min.Y; y < outer.Max.Y; y++)
            for (int z = outer.Min.Z; z < outer.Max.Z; z++)
                for (int x = outer.Min.X; x < outer.Max.X; x++)
                {
                    var cell = new Int3(x, y, z);
                    if (Wires.Contains(cell) && seen.Add(cell)) queue.Enqueue(cell);
                }
        var result = new List<Int3>();
        while (queue.Count > 0 && result.Count < MaxSignalCells)
        {
            var c = queue.Dequeue();
            result.Add(c);
            foreach (var o in Faces.Offsets)
            {
                var n = c + o;
                if (Wires.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return result;
    }

    private bool Activate(TileObjectInstance obj)
    {
        var def = obj.Def;
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
        // Traps shoot out of their front face, which faces whoever placed them: rotation 0 faces −Z, 1 +X, 2 +Z, 3 −X.
        var facing = (obj.Rotation & 3) switch { 0 => -Vector3.UnitZ, 1 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitX };
        var b = obj.Bounds;
        var center = new Vector3(b.Min.X + b.Max.X, b.Min.Y + b.Max.Y, b.Min.Z + b.Max.Z) * 0.5f * Units.CellSize;
        var from = center + facing * (Units.CellSize * 0.6f);
        _world.Projectiles.Spawn(projectile, from, facing * trap.Speed, null, trap.Damage, 2f, DamageClass.None, 0);
        return true;
    }
}
