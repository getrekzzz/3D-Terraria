using Strata.Core.Common;
using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Structures.Dungeon;

/// <summary>Shape of a dungeon hall segment.</summary>
public enum HallKind
{
    /// <summary>A level corridor.</summary>
    Flat,

    /// <summary>A corridor descending one cell per cell travelled (one interior box per step).</summary>
    Stairs,

    /// <summary>A vertical drop; <see cref="DungeonHall.Dir"/> is (0, -1, 0).</summary>
    Shaft,
}

/// <summary>What a room (or node box) is for; the decorator furnishes by role.</summary>
public enum RoomKind
{
    /// <summary>The surface entrance keep (room 0, node 0).</summary>
    Keep,

    /// <summary>A small open crossing where halls meet (no furnishing role).</summary>
    Junction,

    /// <summary>A plain hall-like room (no special role yet).</summary>
    Hall,
    Library,
    Treasure,
    Barracks,
    SpikePit,

    /// <summary>The deepest room; reserves six biome-chest spots (<see cref="DungeonLayout.VaultChestSpots"/>).</summary>
    Vault,

    /// <summary>The <c>bound_npc</c> site: the room nearest the median depth, where the bound Mechanic waits at
    /// <see cref="DungeonLayout.MechanicCell"/>.</summary>
    Mechanic,

    /// <summary>A small side room off a long flat hall, joined by a 3 x 5 doorway.</summary>
    Alcove,
}

/// <summary>
/// One hall segment between two nodes. <see cref="Start"/> is the centre-line floor cell (the lowest air cell) at the
/// segment's beginning; the cross-section is <see cref="Width"/> cells across (centred, extra cell on the + side) and
/// <see cref="Height"/> cells of headroom above the floor. Flat and stair halls cover k = 0..Length inclusive along
/// <see cref="Dir"/>; a shaft is a Width x Width hole from <see cref="End"/>.Y up to Start.Y + Height.
/// </summary>
public sealed record DungeonHall(int Id, Int3 Start, Int3 Dir, int Length, HallKind Kind, int Width, int Height,
    int FromNode, int ToNode, int PanelStyle)
{
    /// <summary>Floor cell at the far end (Flat: same height; Stairs: Length lower; Shaft: Length straight down).</summary>
    public Int3 End => Kind == HallKind.Stairs ? Start + Dir * Length + Int3.Down * Length : Start + Dir * Length;

    /// <summary>Lowest air cell of the floor at step / distance k (0..Length).</summary>
    public int FloorY(int k) => Kind == HallKind.Flat ? Start.Y : Start.Y - k;

    public int InteriorCount => Kind == HallKind.Stairs ? Length + 1 : 1;

    /// <summary>Interior box <paramref name="i"/> (stairs: step i; otherwise the whole segment).</summary>
    public Aabb3i Interior(int i)
    {
        if (Kind == HallKind.Shaft)
        {
            var lo = new Int3(Start.X - Width / 2, Start.Y - Length, Start.Z - Width / 2);
            return new Aabb3i(lo, new Int3(lo.X + Width, Start.Y + Height, lo.Z + Width));
        }
        int a0, a1;
        int s = Dir.X != 0 ? Start.X : Start.Z, d = Dir.X != 0 ? Dir.X : Dir.Z;
        if (Kind == HallKind.Stairs) { a0 = s + d * i; a1 = a0 + 1; }
        else if (d > 0) { a0 = s; a1 = s + Length + 1; }
        else { a0 = s - Length; a1 = s + 1; }
        int y = FloorY(Kind == HallKind.Stairs ? i : 0);
        int c = Dir.X != 0 ? Start.Z : Start.X, c0 = c - Width / 2;
        return Dir.X != 0
            ? new Aabb3i(new Int3(a0, y, c0), new Int3(a1, y + Height, c0 + Width))
            : new Aabb3i(new Int3(c0, y, a0), new Int3(c0 + Width, y + Height, a1));
    }

    /// <summary>Every interior box (one per stair step).</summary>
    public Aabb3i[] Interiors()
    {
        var boxes = new Aabb3i[InteriorCount];
        for (int i = 0; i < boxes.Length; i++) boxes[i] = Interior(i);
        return boxes;
    }

    public Aabb3i Bounds
    {
        get
        {
            var b = Aabb3i.Empty;
            for (int i = 0; i < InteriorCount; i++) b = b.Union(Interior(i));
            return b;
        }
    }
}

/// <summary>An opening through a room's 2-cell shell: <see cref="Box"/> is the cut, <see cref="Normal"/> points out of
/// the room, <see cref="Hall"/> is the hall it joins (-1 for none).</summary>
public readonly record struct DungeonDoorway(Aabb3i Box, Int3 Normal, int Hall);

/// <summary>A room (or a junction / the keep) at a graph node. <see cref="Interior"/> is the air volume; the
/// floor is Interior.Min.Y.</summary>
public sealed record DungeonRoom(int Id, Aabb3i Interior, RoomKind Kind, int Node, int PanelStyle, IReadOnlyList<DungeonDoorway> Doorways)
{
    /// <summary>Floor cell at the centre of the room.</summary>
    public Int3 CenterFloor => new((Interior.Min.X + Interior.Max.X) / 2, Interior.Min.Y, (Interior.Min.Z + Interior.Max.Z) / 2);
}

/// <summary>A graph node: <see cref="Floor"/> is its box's centre floor cell; <see cref="Room"/> indexes Rooms.</summary>
public sealed record DungeonNode(int Id, Int3 Floor, int Room, bool Branch);

/// <summary>An undirected link joined by <see cref="Hall"/>. For a side room (<see cref="SideDoor"/>) B is the room's
/// node and the link is a doorway from the middle of <see cref="Hall"/> (which starts at A).</summary>
public readonly record struct DungeonEdge(int A, int B, int Hall, bool SideDoor = false);

/// <summary>A chest site: <see cref="Cell"/> is the floor cell (lowest air cell) for the chest's anchor,
/// <see cref="Facing"/> the horizontal direction it should face. Exactly one gold-chest spot holds the Shadow Key.</summary>
public readonly record struct DungeonChestSpot(Int3 Cell, int Room, Int3 Facing, bool ShadowKey);

/// <summary>The planned dungeon: pure data, consumed by the carver, keep builder and decorator.</summary>
public sealed class DungeonLayout
{
    /// <summary>Air volume of the entrance keep (node 0, room 0).</summary>
    public required Aabb3i KeepInterior { get; init; }

    /// <summary>Floor cell of the keep's doorway, the first wall cell outside the interior on the inward face.</summary>
    public required Int3 Door { get; init; }

    /// <summary>Horizontal unit axis toward the world centre; the door faces it and the trunk never heads against it.</summary>
    public required Int3 Inward { get; init; }

    /// <summary>Hall 0 is the entrance shaft from the keep floor down to the trunk.</summary>
    public required IReadOnlyList<DungeonHall> Halls { get; init; }
    public required IReadOnlyList<DungeonRoom> Rooms { get; init; }
    public required IReadOnlyList<DungeonNode> Nodes { get; init; }
    public required IReadOnlyList<DungeonEdge> Edges { get; init; }

    /// <summary>Locked gold-chest spots (treasure rooms, alcoves); exactly one has ShadowKey.</summary>
    public required IReadOnlyList<DungeonChestSpot> ChestSpots { get; init; }

    /// <summary>Six reserved biome-chest spots inside the vault.</summary>
    public required IReadOnlyList<DungeonChestSpot> VaultChestSpots { get; init; }

    /// <summary>The <c>bound_npc</c> site: floor cell where the bound Mechanic stands (in <see cref="MechanicRoom"/>).</summary>
    public required Int3 MechanicCell { get; init; }
    public required int MechanicRoom { get; init; }
    public required int VaultRoom { get; init; }

    /// <summary>Lowest allowed interior floor (from the budget).</summary>
    public required int FloorLimit { get; init; }

    /// <summary>Hall cells achieved by the trunk and by the branches (for reports).</summary>
    public required int TrunkCells { get; init; }
    public required int BranchCells { get; init; }

    /// <summary>Union of every interior grown by the 2-cell brick shell.</summary>
    public required Aabb3i Bounds { get; init; }

    /// <summary>Stable hash over the whole layout (same seed and site, same hash).</summary>
    public ulong Hash()
    {
        ulong h = Determinism.Hash.String("dungeon_layout");
        h = Box(h, KeepInterior);
        h = P(h, Door);
        h = P(h, Inward);
        foreach (var hall in Halls)
        {
            h = P(P(h, hall.Start), hall.Dir);
            h = Determinism.Hash.Combine(h, ((long)hall.Length << 40) ^ ((long)hall.Kind << 32) ^ ((long)hall.Width << 24)
                ^ ((long)hall.Height << 16) ^ ((long)hall.PanelStyle << 12) ^ ((long)hall.FromNode << 6) ^ hall.ToNode);
        }
        foreach (var room in Rooms)
        {
            h = Determinism.Hash.Combine(Box(h, room.Interior), ((long)room.Kind << 32) ^ ((long)room.PanelStyle << 24) ^ room.Node);
            foreach (var d in room.Doorways) h = Determinism.Hash.Combine(P(Box(h, d.Box), d.Normal), d.Hall);
        }
        foreach (var e in Edges) h = Determinism.Hash.Combine(h, ((long)e.A << 40) ^ ((long)e.B << 20) ^ (e.Hall + 1) ^ (e.SideDoor ? 1L << 62 : 0));
        foreach (var s in ChestSpots) h = Determinism.Hash.Combine(P(P(h, s.Cell), s.Facing), s.Room * 2 + (s.ShadowKey ? 1 : 0));
        foreach (var s in VaultChestSpots) h = P(P(h, s.Cell), s.Facing);
        h = P(h, MechanicCell);
        return Determinism.Hash.Combine(h, ((long)MechanicRoom << 32) ^ VaultRoom);
    }

    private static ulong P(ulong h, Int3 p) => Determinism.Hash.Cell(h, p.X, p.Y, p.Z);

    private static ulong Box(ulong h, Aabb3i b) => P(P(h, b.Min), b.Max);
}
