using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>
/// Furnishes a carved dungeon blueprint (after <see cref="DungeonBuilder"/>): rooms by role in the dungeon colour's
/// furniture set, banners, lamps, candelabras, pots and cobwebs along the halls, spike strips, and dart traps on
/// pressure plates joined by planned wires. Every placement is checked against what the blueprint will leave
/// (<see cref="BlueprintProbe"/>): footprints in air, floor objects on solid floor, wall objects against a solid wall,
/// ceiling objects under a solid ceiling; no two objects share a cell (across chunk borders too), doorways, hall
/// mouths, the bound Mechanic's spot and the vault's chest spots stay clear, and every chunk keeps to
/// <see cref="ObjectBudget"/> objects, <see cref="LightBudget"/> lights and <see cref="SpikeBudget"/> spikes. One
/// rng stream (<c>dungeon.decor</c>), so the same seed furnishes the same way.
/// </summary>
public static partial class DungeonDecorator
{
    public const int ObjectBudget = 40, LightBudget = 6, SpikeBudget = 64;

    /// <summary>About one shelf book in this many is a Water Bolt (at least one per dungeon).</summary>
    public const int WaterBoltOdds = 40;

    public const string WaterBoltLoot = "water_bolt_book";

    /// <summary>Decorates the open blueprint and returns the planned wire cells (plates to their traps).</summary>
    public static HashSet<Int3> Decorate(ContentDatabase content, Blueprint bp, DungeonLayout layout, DungeonStyle style, SeedTree seeds)
    {
        var d = new Decor(content, bp, layout, style, seeds.Rng("dungeon.decor"));
        foreach (var room in layout.Rooms) d.ReserveRoom(room);
        Keep(d);
        foreach (var room in layout.Rooms) Room(d, room);
        foreach (var hall in layout.Halls) Hall(d, hall);
        FallbackBook(d);
        d.Finish();
        return d.Wires;
    }

    /// <summary>How a placed object is held up (<see cref="Hold.Rests"/>: on an object the caller just placed).</summary>
    internal enum Hold { Floor, Ceiling, Wall, Shelf, Rests }

    /// <summary>Shared decorating state.</summary>
    internal sealed class Decor
    {
        private readonly Blueprint _bp;
        private readonly HashSet<Int3> _occupied = new();
        private readonly HashSet<Int3> _reserved = new();
        private readonly Dictionary<Int3, int> _objects = new(), _lights = new(), _spikes = new();
        private readonly List<BlueprintOp> _pending = new();
        private readonly List<int> _bookOps = new();
        public Pcg32 Rng;

        public Decor(ContentDatabase content, Blueprint bp, DungeonLayout layout, DungeonStyle style, Pcg32 rng)
        {
            _bp = bp;
            Content = content;
            Layout = layout;
            Style = style;
            Rng = rng;
            Probe = new BlueprintProbe(content, bp);
            foreach (var op in bp.Ops)
                if (op is ObjectOp o)
                {
                    Occupy(o.Bounds);
                    Count(content.TileObjects.Get(o.ObjectId), o.Anchor);
                }
        }

        public ContentDatabase Content { get; }
        public DungeonLayout Layout { get; }
        public DungeonStyle Style { get; }
        public BlueprintProbe Probe { get; }
        public HashSet<Int3> Wires { get; } = new();
        public int Books => _bookOps.Count;
        public byte Platform { get; } = ShapeCode.Pack(CellShape.Platform, 0);

        /// <summary>The colour's own piece of a furniture set ("table" → "blue_dungeon_table").</summary>
        public TileObjectDef Set(string piece) => Content.TileObjects.Get(Style.Colour + "_dungeon_" + piece);

        public TileObjectDef Def(string id) => Content.TileObjects.Get(id);

        /// <summary>Keeps doorways (3 cells in), hall mouths and shafts through a room, the Mechanic's spot and the
        /// vault's chest spots free of objects.</summary>
        public void ReserveRoom(DungeonRoom room)
        {
            var r = room.Interior;
            foreach (var door in room.Doorways) Reserve(door.Box.Expand(3).Intersection(r));
            var node = Layout.Nodes[room.Node];
            foreach (var hall in Layout.Halls)
            {
                if (hall.FromNode != node.Id && hall.ToNode != node.Id) continue;
                for (int i = 0; i < hall.InteriorCount; i++) Reserve(hall.Interior(i).Expand(1).Intersection(r));
            }
            if (room.Id == Layout.MechanicRoom)
            {
                var m = Layout.MechanicCell;
                Reserve(new Aabb3i(m - new Int3(2, 0, 2), m + new Int3(3, 5, 3)).Intersection(r));
            }
            foreach (var spot in Layout.VaultChestSpots)
                if (spot.Room == room.Id) Reserve(ChestBox(spot).Expand(1).Intersection(r));
        }

        /// <summary>The cells a chest on a planned spot stands on (its footprint kept inside the room).</summary>
        public Aabb3i ChestBox(DungeonChestSpot spot)
        {
            var anchor = DungeonBuilder.ChestAnchor(Content, Layout, spot);
            return new Aabb3i(anchor, anchor + TileObjectInstance.FootprintOf(Def("gold_chest"), DungeonBuilder.RotationToward(spot.Facing)));
        }

        private void Reserve(Aabb3i box)
        {
            if (box.IsEmpty) return;
            for (int y = box.Min.Y; y < box.Max.Y; y++)
                for (int z = box.Min.Z; z < box.Max.Z; z++)
                    for (int x = box.Min.X; x < box.Max.X; x++) _reserved.Add(new Int3(x, y, z));
        }

        private void Occupy(Aabb3i box)
        {
            for (int y = box.Min.Y; y < box.Max.Y; y++)
                for (int z = box.Min.Z; z < box.Max.Z; z++)
                    for (int x = box.Min.X; x < box.Max.X; x++) _occupied.Add(new Int3(x, y, z));
        }

        private void Count(TileObjectDef def, Int3 anchor)
        {
            var chunk = ChunkMath.CellToChunk(anchor);
            var counter = def.Hazard != null ? _spikes : _objects;
            counter[chunk] = counter.GetValueOrDefault(chunk) + 1;
            if (IsLight(def)) _lights[chunk] = _lights.GetValueOrDefault(chunk) + 1;
        }

        public static bool IsLight(TileObjectDef def) => def.Light is { Length: 3 } l && (l[0] | l[1] | l[2]) != 0;

        /// <summary>Whether a box is all carved air, free and unreserved (<paramref name="reserved"/> false skips
        /// the reservation check, for the pieces the reservations are for).</summary>
        public bool Clear(Aabb3i box, bool reserved = true)
        {
            for (int y = box.Min.Y; y < box.Max.Y; y++)
                for (int z = box.Min.Z; z < box.Max.Z; z++)
                    for (int x = box.Min.X; x < box.Max.X; x++)
                    {
                        var c = new Int3(x, y, z);
                        if (_occupied.Contains(c) || (reserved && _reserved.Contains(c)) || !Probe.IsAir(c)) return false;
                    }
            return true;
        }

        /// <summary>Whether every cell of a box is a full solid block (or, for shelves, a platform).</summary>
        public bool Solid(Aabb3i box, bool shaped = false)
        {
            for (int y = box.Min.Y; y < box.Max.Y; y++)
                for (int z = box.Min.Z; z < box.Max.Z; z++)
                    for (int x = box.Min.X; x < box.Max.X; x++)
                    {
                        var s = Probe.At(new Int3(x, y, z));
                        if (s != ProbeCell.Solid && !(shaped && s == ProbeCell.Shaped)) return false;
                    }
            return true;
        }

        /// <summary>
        /// Places an object if its footprint is clear, it is held up the <paramref name="hold"/> way (a wall object
        /// against <paramref name="wall"/>, the outward direction of the wall it hangs on) and its chunk has budget.
        /// </summary>
        public bool TryPlace(TileObjectDef def, Int3 anchor, int rotation, Hold hold, Int3 wall = default, int variant = 0, string? loot = null, bool reserved = true, bool locked = false)
        {
            var size = TileObjectInstance.FootprintOf(def, rotation);
            var box = new Aabb3i(anchor, anchor + size);
            if (!Clear(box, reserved)) return false;
            bool held = hold switch
            {
                Hold.Floor => Solid(new Aabb3i(new Int3(box.Min.X, box.Min.Y - 1, box.Min.Z), new Int3(box.Max.X, box.Min.Y, box.Max.Z))),
                Hold.Shelf => Solid(new Aabb3i(new Int3(box.Min.X, box.Min.Y - 1, box.Min.Z), new Int3(box.Max.X, box.Min.Y, box.Max.Z)), shaped: true),
                Hold.Ceiling => Solid(new Aabb3i(new Int3(box.Min.X, box.Max.Y, box.Min.Z), new Int3(box.Max.X, box.Max.Y + 1, box.Max.Z))),
                Hold.Wall => Solid(Behind(box, wall)),
                _ => true, // rests on an object the caller just placed (a candle on a table)
            };
            if (!held || !HasBudget(def, anchor)) return false;
            Occupy(box);
            Count(def, anchor);
            int at = _pending.Count;
            _pending.Add(new ObjectOp(box, def.Id, anchor, rotation, variant, loot, locked));
            if (def.Id == "book") _bookOps.Add(at);
            return true;
        }

        /// <summary>The cells behind a box on its <paramref name="wall"/> side (the wall it hangs on).</summary>
        public static Aabb3i Behind(Aabb3i box, Int3 wall) => wall.X > 0 ? new Aabb3i(new Int3(box.Max.X, box.Min.Y, box.Min.Z), new Int3(box.Max.X + 1, box.Max.Y, box.Max.Z))
            : wall.X < 0 ? new Aabb3i(new Int3(box.Min.X - 1, box.Min.Y, box.Min.Z), new Int3(box.Min.X, box.Max.Y, box.Max.Z))
            : wall.Z > 0 ? new Aabb3i(new Int3(box.Min.X, box.Min.Y, box.Max.Z), new Int3(box.Max.X, box.Max.Y, box.Max.Z + 1))
            : new Aabb3i(new Int3(box.Min.X, box.Min.Y, box.Min.Z - 1), new Int3(box.Max.X, box.Max.Y, box.Min.Z));

        public bool HasBudget(TileObjectDef def, Int3 anchor)
        {
            var chunk = ChunkMath.CellToChunk(anchor);
            if (def.Hazard != null) return _spikes.GetValueOrDefault(chunk) < SpikeBudget;
            if (_objects.GetValueOrDefault(chunk) >= ObjectBudget) return false;
            return !IsLight(def) || _lights.GetValueOrDefault(chunk) < LightBudget;
        }

        /// <summary>Adds a block fill (shelves, a dais, cobwebs) that later placements see.</summary>
        public void Fill(Aabb3i box, string block, byte shape = 0, bool onlyAir = false)
        {
            var op = new FillOp(box, block, shape, onlyAir);
            _pending.Add(op);
            Probe.Add(op);
        }

        /// <summary>Carves a cell (a dart trap's slot in a wall) that later placements see.</summary>
        public void Carve(Aabb3i box)
        {
            var op = new CarveOp(box);
            _pending.Add(op);
            Probe.Add(op);
        }

        /// <summary>A cobweb cluster where the box is still clear air.</summary>
        public void Cobweb(Aabb3i box)
        {
            if (Clear(box, reserved: false)) Fill(box, "cobweb", 0, onlyAir: true);
        }

        /// <summary>Rolls the Water Bolts among the shelf books (at least one when any book was placed) and appends
        /// every op to the blueprint.</summary>
        public void Finish()
        {
            bool any = false;
            foreach (int i in _bookOps)
                if (Rng.NextInt(WaterBoltOdds) == 0)
                {
                    _pending[i] = ((ObjectOp)_pending[i]) with { Loot = WaterBoltLoot };
                    any = true;
                }
            if (!any && _bookOps.Count > 0)
            {
                int i = _bookOps[Rng.NextInt(_bookOps.Count)];
                _pending[i] = ((ObjectOp)_pending[i]) with { Loot = WaterBoltLoot };
            }
            foreach (var op in _pending) _bp.Add(op);
        }
    }

    /// <summary>Quarter turns pointing an object's front at a horizontal direction (as <see cref="DungeonBuilder.RotationToward"/>).</summary>
    internal static int Facing(Int3 dir) => DungeonBuilder.RotationToward(dir);

    /// <summary>Quarter turns that make a dart trap shoot toward <paramref name="dir"/> (WireSystem.FireTrap: rotation
    /// 0 shoots −Z, 1 +X, 2 +Z, 3 −X).</summary>
    internal static int TrapRotation(Int3 dir) => dir.Z < 0 ? 0 : dir.X > 0 ? 1 : dir.Z > 0 ? 2 : 3;
}
