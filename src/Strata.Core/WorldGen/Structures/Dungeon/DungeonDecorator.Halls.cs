using Strata.Core.Common;

namespace Strata.Core.WorldGen.Structures.Dungeon;

public static partial class DungeonDecorator
{
    private const float SpikeStripChance = 0.15f, TrapChance = 0.10f, CobwebChance = 0.4f;
    private const int TrapHeight = 2, SpikeStripLength = 3;

    /// <summary>
    /// A flat hall: banners, lamps and candelabras in turn every 12–16 cells, 1–3 clusters of pots against the walls,
    /// cobwebs in the upper corners, and either a jumpable spike strip (15%) or a pressure plate whose wire runs under
    /// the floor and up the side wall to a dart trap aimed across the hall (10%). Stairs and shafts only gather
    /// cobwebs. Nothing goes where the hall runs through a room or junction box.
    /// </summary>
    private static void Hall(Decor d, DungeonHall hall)
    {
        if (hall.Kind == HallKind.Shaft) return;
        if (hall.Kind == HallKind.Stairs)
        {
            for (int k = 2; k < hall.Length - 2; k += 12)
                foreach (int side in Sides)
                    if (d.Rng.Chance(CobwebChance) && Outside(d, hall, k) && Outside(d, hall, k + 1))
                        d.Cobweb(HallBox(hall, k, side, hall.FloorY(k) - hall.Start.Y + hall.Height - 2, 1, 2, 2));
            return;
        }
        if (hall.Length < 8) return;
        int kind = d.Rng.NextInt(3);
        for (int k = 3 + d.Rng.NextInt(6); k < hall.Length - 3; k += d.Rng.Range(12, 16), kind = (kind + 1) % 3)
        {
            int side = d.Rng.Chance(0.5f) ? -1 : 1;
            if (!Outside(d, hall, k) || !Outside(d, hall, k + 1)) continue;
            var wall = Cross(hall) * side;
            int rot = Facing(-wall);
            switch (kind)
            {
                case 0:
                    // A banner on each wall, high up.
                    foreach (int s in Sides)
                    {
                        var w = Cross(hall) * s;
                        d.TryPlace(d.Def(Banner(d)), HallBox(hall, k, s, 4, 2, 1, 4).Min, Facing(-w), Hold.Wall, w);
                    }
                    break;
                case 1:
                    d.TryPlace(d.Set("lamp"), HallBox(hall, k, side, 0, 1, 1, 4).Min, rot, Hold.Floor);
                    break;
                default:
                    d.TryPlace(d.Set("candelabra"), HallBox(hall, k, side, 0, 2, 1, 2).Min, rot, Hold.Floor);
                    break;
            }
        }
        var pot = d.Def("pot");
        for (int cluster = d.Rng.Range(1, 3); cluster > 0; cluster--)
        {
            int k = d.Rng.Range(2, hall.Length - 4), side = d.Rng.Chance(0.5f) ? -1 : 1;
            for (int n = d.Rng.Range(1, 3); n > 0; n--, k += 2)
                if (Outside(d, hall, k) && Outside(d, hall, k + 1))
                    d.TryPlace(pot, HallBox(hall, k, side, 0, 2, 2, 2).Min, d.Rng.NextInt(4), Hold.Floor, variant: d.Rng.NextInt(pot.Variants));
        }
        for (int k = 1; k < hall.Length - 1; k += 12)
            foreach (int side in Sides)
                if (d.Rng.Chance(CobwebChance) && Outside(d, hall, k) && Outside(d, hall, k + 1))
                    d.Cobweb(HallBox(hall, k, side, hall.Height - 2, 2, 2, 2));

        if (hall.Length >= 20 && d.Rng.Chance(SpikeStripChance)) SpikeStrip(d, hall);
        else if (hall.Length >= 16 && d.Rng.Chance(TrapChance)) DartTrap(d, hall);
    }

    private static readonly int[] Sides = { -1, 1 };

    /// <summary>The horizontal unit axis across a hall (+X for a hall along Z, +Z for a hall along X).</summary>
    private static Int3 Cross(DungeonHall hall) => hall.Dir.X != 0 ? new Int3(0, 0, 1) : new Int3(1, 0, 0);

    /// <summary>Cross offset (from the centre line) of a hall's wall-side interior column.</summary>
    private static int SideOffset(DungeonHall hall, int side) => side < 0 ? -(hall.Width / 2) : hall.Width - 1 - hall.Width / 2;

    /// <summary>A hall cell: <paramref name="k"/> along, <paramref name="cross"/> across from the centre line,
    /// <paramref name="h"/> over the hall's starting floor.</summary>
    private static Int3 HallCell(DungeonHall hall, int k, int cross, int h) => hall.Start + hall.Dir * k + Cross(hall) * cross + Int3.Up * h;

    /// <summary>A box against one side wall: from step <paramref name="k"/>, <paramref name="along"/> cells along the
    /// hall, <paramref name="depth"/> cells in from the wall and <paramref name="height"/> tall from <paramref name="h"/>.</summary>
    private static Aabb3i HallBox(DungeonHall hall, int k, int side, int h, int along, int depth, int height)
    {
        int c = SideOffset(hall, side);
        var a = HallCell(hall, k, c, h);
        var b = HallCell(hall, k + along - 1, c - side * (depth - 1), h + height - 1);
        return new Aabb3i(Min(a, b), Max(a, b) + Int3.One);
    }

    private static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    /// <summary>Whether step <paramref name="k"/> of a hall lies outside the room or junction boxes at its ends.</summary>
    private static bool Outside(Decor d, DungeonHall hall, int k)
    {
        if (k < 0 || k > hall.Length) return false;
        var c = HallCell(hall, k, 0, hall.Kind == HallKind.Stairs ? hall.FloorY(k) - hall.Start.Y : 0);
        foreach (int node in stackalloc[] { hall.FromNode, hall.ToNode })
        {
            if (node < 0) continue;
            int room = d.Layout.Nodes[node].Room;
            if (room >= 0 && d.Layout.Rooms[room].Interior.Expand(3).Contains(c)) return false;
        }
        return true;
    }

    private static string Banner(Decor d) => BannerIds[d.Rng.NextInt(BannerIds.Length)];

    private static readonly string[] BannerIds =
        { "marching_bones_banner", "necromantic_sign", "rusted_company_standard", "ragged_brotherhood_sigil", "molten_legion_flag", "diabolic_sigil" };

    /// <summary>A centred strip of spikes <see cref="SpikeStripLength"/> cells long across the whole hall (short
    /// enough to jump), only where every cell of it is clear.</summary>
    private static void SpikeStrip(Decor d, DungeonHall hall)
    {
        int k0 = hall.Length / 2 - 1;
        var spike = d.Def("spike");
        int lo = SideOffset(hall, -1), hi = SideOffset(hall, 1);
        for (int k = k0; k < k0 + SpikeStripLength; k++)
        {
            if (!Outside(d, hall, k)) return;
            for (int c = lo; c <= hi; c++)
            {
                var cell = HallCell(hall, k, c, 0);
                if (!d.Clear(new Aabb3i(cell, cell + Int3.One)) || !d.Solid(new Aabb3i(cell - Int3.Up, cell))) return;
            }
        }
        for (int k = k0; k < k0 + SpikeStripLength; k++)
            for (int c = lo; c <= hi; c++) d.TryPlace(spike, HallCell(hall, k, c, 0), 0, Hold.Floor);
    }

    /// <summary>
    /// A pressure plate on the centre line and a dart trap set into one side wall at chest height, aimed across the
    /// hall at the plate. The wire runs from under the plate, under the floor to the wall, then up the wall to the trap
    /// (the trap's slot is carved out of the panel; the brick behind it stays).
    /// </summary>
    private static void DartTrap(Decor d, DungeonHall hall)
    {
        int k = hall.Length / 2 + d.Rng.Range(-3, 3);
        int side = d.Rng.Chance(0.5f) ? -1 : 1;
        if (!Outside(d, hall, k)) return;
        var outward = Cross(hall) * side;
        int wallOff = SideOffset(hall, side) + side;
        var plate = HallCell(hall, k, 0, 0);
        var trap = HallCell(hall, k, wallOff, TrapHeight);
        var plateDef = d.Def("pressure_plate");
        var trapDef = d.Def("dart_trap");
        // The slot must be wall (panel) with wall behind it, and every wire cell solid brick or panel.
        if (!d.Solid(new Aabb3i(trap, trap + Int3.One)) || !d.Solid(new Aabb3i(trap + outward, trap + outward + Int3.One))) return;
        var wire = new List<Int3>();
        for (int c = 0; c != wallOff + side; c += side) wire.Add(HallCell(hall, k, c, -1));
        for (int h = 0; h <= TrapHeight; h++) wire.Add(HallCell(hall, k, wallOff, h));
        foreach (var w in wire)
            if (w != trap && !d.Solid(new Aabb3i(w, w + Int3.One))) return;
        if (!d.HasBudget(trapDef, trap) || !d.TryPlace(plateDef, plate, 0, Hold.Floor)) return;
        d.Carve(new Aabb3i(trap, trap + Int3.One));
        if (!d.TryPlace(trapDef, trap, TrapRotation(-outward), Hold.Wall, outward, reserved: false))
        {
            d.Fill(new Aabb3i(trap, trap + Int3.One), d.Style.Panel(hall.PanelStyle)); // put the panel back
            return;
        }
        foreach (var w in wire) d.Wires.Add(w);
    }
}
